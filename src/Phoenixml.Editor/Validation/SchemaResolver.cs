using System.IO;
using System.Xml.Schema;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Editor.Settings;
using Phoenixml.Platform.Editor.Xml;

namespace Phoenixml.Editor.Validation;

/// <summary>
/// Resolves an <see cref="XmlSchemaSet"/> for a given document.
/// </summary>
/// <remarks>
/// Resolution order:
/// <list type="number">
///   <item>A per-document override path stored in <see cref="EditorSettings.PerDocumentSchema"/> keyed by document path.</item>
///   <item><c>xsi:noNamespaceSchemaLocation</c> or <c>xsi:schemaLocation</c> attributes on the document root element.</item>
/// </list>
/// Returns <see langword="null"/> when no schema is found; callers should skip validation silently.
/// </remarks>
internal sealed class SchemaResolver
{
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
    /// A compiled schema set, or <see langword="null"/> if no schema hint is available
    /// or the referenced XSD is missing or corrupt.
    /// </returns>
    public XmlSchemaSet? Resolve(XmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // 1. Per-document override stored in settings.
        var path = document.Source?.LocalPath;
        if (path is not null
            && _settings.Settings.PerDocumentSchema.TryGetValue(path, out var xsdPath)
            && File.Exists(xsdPath))
        {
            var overrideSet = _cache.GetOrLoad(xsdPath);
            if (overrideSet is not null) return overrideSet;
            // corrupt or unreadable — fall through to xsi: hints
        }

        // 2. xsi: hints on the document root element.
        var docElementId = document.Root.DocumentElement;
        if (docElementId is null) return null;

        var rootEl = document.Resolve(docElementId.Value) as XdmElement;
        if (rootEl is null) return null;

        foreach (var attrId in rootEl.Attributes)
        {
            var attr = document.Resolve(attrId) as XdmAttribute;
            if (attr is null) continue;

            // XdmAttribute.Namespace is a NamespaceId, not a string.
            // NamespaceId.Xsi is the pre-seeded intern for http://www.w3.org/2001/XMLSchema-instance.
            if (attr.Namespace != NamespaceId.Xsi) continue;

            if (attr.LocalName == "noNamespaceSchemaLocation")
            {
                return TryLoad(attr.Value, document);
            }

            if (attr.LocalName == "schemaLocation")
            {
                // Value is "namespace location [namespace location ...]" pairs.
                // Take the first pair's location (index 1).
                var parts = attr.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) return TryLoad(parts[1], document);
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to load a schema from <paramref name="location"/>, resolving relative paths
    /// against the directory containing <paramref name="document"/>'s source file.
    /// Returns <see langword="null"/> on any error.
    /// </summary>
    private XmlSchemaSet? TryLoad(string location, XmlDocument document)
    {
        var docDir = Path.GetDirectoryName(document.Source?.LocalPath);
        var full = Path.IsPathRooted(location) || docDir is null
            ? location
            : Path.Combine(docDir, location);

        return _cache.GetOrLoad(full);
    }
}
