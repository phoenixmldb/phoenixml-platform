using System.IO;
using System.Xml.Schema;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Workbench.Settings;
using Phoenixml.Platform.Editor.Xml;

namespace Phoenixml.Editor.Validation;

/// <summary>
/// Describes how a schema was resolved so callers can reason about priority and source.
/// </summary>
internal enum SchemaResolutionKind
{
    /// <summary>A per-document path stored in <see cref="EditorSettings.PerDocumentSchema"/>.</summary>
    Override,

    /// <summary>An <c>xsi:noNamespaceSchemaLocation</c> or <c>xsi:schemaLocation</c> attribute.</summary>
    XsiHint,

    /// <summary><c>&lt;dir&gt;/&lt;base&gt;.xsd</c> — same folder, name match.</summary>
    AutoSameFolderName,

    /// <summary><c>&lt;dir&gt;/*.xsd</c> — same folder, exactly one XSD, no name match.</summary>
    AutoSameFolderSingle,

    /// <summary><c>&lt;dir&gt;/schemas/&lt;base&gt;.xsd</c> or <c>&lt;dir&gt;/xsd/&lt;base&gt;.xsd</c> — subfolder name match.</summary>
    AutoSubfolderName,

    /// <summary><c>&lt;dir&gt;/schemas/*.xsd</c> or <c>&lt;dir&gt;/xsd/*.xsd</c> — subfolder, exactly one XSD.</summary>
    AutoSubfolderSingle,

    /// <summary><c>&lt;dir&gt;/../schemas/&lt;base&gt;.xsd</c> — sibling-folder name match.</summary>
    AutoSiblingFolderName,

    /// <summary><c>&lt;dir&gt;/../schemas/*.xsd</c> — sibling-folder, exactly one XSD.</summary>
    AutoSiblingFolderSingle,
}

/// <summary>
/// Pairs a resolved <see cref="XmlSchemaSet"/> with provenance information.
/// </summary>
/// <param name="Schemas">The compiled schema set.</param>
/// <param name="SourcePath">Absolute path of the XSD file that was loaded.</param>
/// <param name="Kind">How the schema was located.</param>
internal sealed record SchemaResolution(XmlSchemaSet Schemas, string SourcePath, SchemaResolutionKind Kind);

/// <summary>
/// Resolves an <see cref="XmlSchemaSet"/> for a given document.
/// </summary>
/// <remarks>
/// Resolution order:
/// <list type="number">
///   <item>A per-document override path stored in <see cref="EditorSettings.PerDocumentSchema"/> keyed by document path.</item>
///   <item><c>xsi:noNamespaceSchemaLocation</c> or <c>xsi:schemaLocation</c> attributes on the document root element.</item>
///   <item>Search-outward auto-detect: same folder → named subfolders → sibling folders.</item>
/// </list>
/// Returns <see langword="null"/> when no schema is found; callers should skip validation silently.
/// </remarks>
internal sealed class SchemaResolver
{
    private static readonly string[] s_subfolderNames = ["schemas", "xsd"];

    private readonly EditorSettingsStore _settings;
    private readonly SchemaCache _cache;

    /// <summary>
    /// Initializes a new <see cref="SchemaResolver"/> backed by the given settings store and
    /// an optional <see cref="SchemaCache"/>.
    /// </summary>
    /// <param name="settings">The settings store providing per-document schema overrides.</param>
    /// <param name="cache">
    /// The schema cache to route all XSD loads through. When <see langword="null"/> a new
    /// private cache is created — existing callers that omit the argument are unaffected.
    /// </param>
    public SchemaResolver(EditorSettingsStore settings, SchemaCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _cache = cache ?? new SchemaCache();
    }

    /// <summary>
    /// Attempts to resolve a compiled <see cref="XmlSchemaSet"/> for <paramref name="document"/>.
    /// </summary>
    /// <param name="document">The document to resolve a schema for.</param>
    /// <returns>
    /// A compiled schema set, or <see langword="null"/> if no schema is found.
    /// </returns>
    public XmlSchemaSet? Resolve(XmlDocument document) => ResolveDetailed(document)?.Schemas;

    /// <summary>
    /// Attempts to resolve a schema and returns full provenance information.
    /// </summary>
    /// <param name="document">The document to resolve a schema for.</param>
    /// <returns>
    /// A <see cref="SchemaResolution"/> describing the resolved schema and how it was found,
    /// or <see langword="null"/> if no schema is available.
    /// </returns>
    internal SchemaResolution? ResolveDetailed(XmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // 1. Per-document override stored in settings.
        var docPath = document.Source?.LocalPath;
        if (docPath is not null
            && _settings.Settings.PerDocumentSchema.TryGetValue(docPath, out var xsdPath)
            && File.Exists(xsdPath))
        {
            var overrideSet = _cache.GetOrLoad(xsdPath);
            if (overrideSet is not null)
                return new SchemaResolution(overrideSet, xsdPath, SchemaResolutionKind.Override);
            // corrupt or unreadable — fall through to xsi: hints
        }

        // 2. xsi: hints on the document root element.
        if (TryResolveXsiHint(document) is SchemaResolution xsiResolution)
            return xsiResolution;

        // 3. Search-outward auto-detect.
        if (docPath is not null && TryAutoDetect(docPath) is SchemaResolution autoResolution)
            return autoResolution;

        return null;
    }

    /// <summary>
    /// Attempts to load a schema from an <c>xsi:noNamespaceSchemaLocation</c> or
    /// <c>xsi:schemaLocation</c> attribute on the document root element.
    /// </summary>
    private SchemaResolution? TryResolveXsiHint(XmlDocument document)
    {
        var docElementId = document.Root.DocumentElement;
        if (docElementId is null) return null;

        var rootEl = document.Resolve(docElementId.Value) as XdmElement;
        if (rootEl is null) return null;

        foreach (var attrId in rootEl.Attributes)
        {
            var attr = document.Resolve(attrId) as XdmAttribute;
            if (attr is null) continue;

            if (attr.Namespace != NamespaceId.Xsi) continue;

            if (attr.LocalName == "noNamespaceSchemaLocation")
            {
                var set = TryLoad(attr.Value, document, out var resolvedPath);
                if (set is not null && resolvedPath is not null)
                    return new SchemaResolution(set, resolvedPath, SchemaResolutionKind.XsiHint);
                return null;
            }

            if (attr.LocalName == "schemaLocation")
            {
                // Value is one or more (namespace, location) pairs, whitespace-separated.
                // Plan 25: load each pair into a single combined XmlSchemaSet so
                // multi-namespace documents validate against all referenced XSDs.
                var parts = attr.Value.Split(
                    [' ', '\t', '\n', '\r'],
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts.Length % 2 != 0) return null;

                var docDir = Path.GetDirectoryName(document.Source?.LocalPath);
                var combined = new XmlSchemaSet();
                string? firstResolvedPath = null;
                try
                {
                    for (int i = 0; i + 1 < parts.Length; i += 2)
                    {
                        var ns = parts[i];
                        var loc = parts[i + 1];
                        var full = Path.IsPathRooted(loc) || docDir is null
                            ? loc
                            : Path.Combine(docDir, loc);
                        if (!File.Exists(full)) return null;
                        var resolved = Path.GetFullPath(full);
                        combined.Add(ns, resolved);
                        firstResolvedPath ??= resolved;
                    }
                    combined.Compile();
                }
                catch (System.Xml.Schema.XmlSchemaException) { return null; }
                catch (IOException) { return null; }

                return firstResolvedPath is null
                    ? null
                    : new SchemaResolution(combined, firstResolvedPath, SchemaResolutionKind.XsiHint);
            }
        }

        return null;
    }

    /// <summary>
    /// Runs the search-outward heuristic for a document at <paramref name="docPath"/>.
    /// Stops at the first match.
    /// </summary>
    private SchemaResolution? TryAutoDetect(string docPath)
    {
        var dir = Path.GetDirectoryName(docPath);
        if (dir is null || !Directory.Exists(dir)) return null;
        var baseName = Path.GetFileNameWithoutExtension(docPath);

        // 1. Same folder, name match.
        var sameNameMatch = Path.Combine(dir, baseName + ".xsd");
        if (File.Exists(sameNameMatch) && _cache.GetOrLoad(sameNameMatch) is XmlSchemaSet a)
            return new SchemaResolution(a, sameNameMatch, SchemaResolutionKind.AutoSameFolderName);

        // 2. Same folder, single XSD (no name match).
        if (TrySingleXsdInFolder(dir) is string sameSingle
            && _cache.GetOrLoad(sameSingle) is XmlSchemaSet b)
            return new SchemaResolution(b, sameSingle, SchemaResolutionKind.AutoSameFolderSingle);

        // 3. Subfolder name match.
        foreach (var subName in s_subfolderNames)
        {
            var sub = Path.Combine(dir, subName);
            if (!Directory.Exists(sub)) continue;
            var subNameMatch = Path.Combine(sub, baseName + ".xsd");
            if (File.Exists(subNameMatch) && _cache.GetOrLoad(subNameMatch) is XmlSchemaSet c)
                return new SchemaResolution(c, subNameMatch, SchemaResolutionKind.AutoSubfolderName);
        }

        // 4. Subfolder, single XSD.
        foreach (var subName in s_subfolderNames)
        {
            var sub = Path.Combine(dir, subName);
            if (!Directory.Exists(sub)) continue;
            if (TrySingleXsdInFolder(sub) is string subSingle
                && _cache.GetOrLoad(subSingle) is XmlSchemaSet d)
                return new SchemaResolution(d, subSingle, SchemaResolutionKind.AutoSubfolderSingle);
        }

        // 5. Sibling-folder name match (the data/ + schemas/ layout).
        var parent = Path.GetDirectoryName(dir);
        if (parent is not null && Directory.Exists(parent))
        {
            foreach (var subName in s_subfolderNames)
            {
                var sib = Path.Combine(parent, subName);
                if (!Directory.Exists(sib)) continue;
                var sibNameMatch = Path.Combine(sib, baseName + ".xsd");
                if (File.Exists(sibNameMatch) && _cache.GetOrLoad(sibNameMatch) is XmlSchemaSet e)
                    return new SchemaResolution(e, sibNameMatch, SchemaResolutionKind.AutoSiblingFolderName);
            }

            // 6. Sibling-folder, single XSD.
            foreach (var subName in s_subfolderNames)
            {
                var sib = Path.Combine(parent, subName);
                if (!Directory.Exists(sib)) continue;
                if (TrySingleXsdInFolder(sib) is string sibSingle
                    && _cache.GetOrLoad(sibSingle) is XmlSchemaSet f)
                    return new SchemaResolution(f, sibSingle, SchemaResolutionKind.AutoSiblingFolderSingle);
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the path of the single XSD in <paramref name="folder"/>, or
    /// <see langword="null"/> if there are zero or more than one XSD files.
    /// </summary>
    private static string? TrySingleXsdInFolder(string folder)
    {
        try
        {
            var xsds = Directory.EnumerateFiles(folder, "*.xsd", SearchOption.TopDirectoryOnly).ToList();
            return xsds.Count == 1 ? xsds[0] : null;
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }

    /// <summary>
    /// Attempts to load a schema from <paramref name="location"/>, resolving relative paths
    /// against the directory containing <paramref name="document"/>'s source file.
    /// Returns <see langword="null"/> on any error.
    /// </summary>
    private XmlSchemaSet? TryLoad(string location, XmlDocument document, out string? resolvedPath)
    {
        var docDir = Path.GetDirectoryName(document.Source?.LocalPath);
        var full = Path.IsPathRooted(location) || docDir is null
            ? location
            : Path.Combine(docDir, location);

        resolvedPath = Path.GetFullPath(full);
        var result = _cache.GetOrLoad(full);
        if (result is null) resolvedPath = null;
        return result;
    }
}
