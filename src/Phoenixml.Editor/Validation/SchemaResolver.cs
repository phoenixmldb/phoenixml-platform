using System.IO;
using System.Xml.Schema;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Editor.Settings;
using Phoenixml.Platform.Editor.Xml;
using EditorXmlSchemaValidator = Phoenixml.Platform.Editor.Xml.Validation.XmlSchemaValidator;

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

    /// <summary>
    /// Initializes a new <see cref="SchemaResolver"/> backed by the given settings store.
    /// </summary>
    /// <param name="settings">The settings store providing per-document schema overrides.</param>
    public SchemaResolver(EditorSettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
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
            try { return EditorXmlSchemaValidator.LoadSchema(xsdPath); }
            catch (XmlSchemaException) { /* corrupt schema — fall through */ }
            catch (IOException) { /* unreadable — fall through */ }
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
    private static XmlSchemaSet? TryLoad(string location, XmlDocument document)
    {
        var docDir = Path.GetDirectoryName(document.Source?.LocalPath);
        var full = Path.IsPathRooted(location) || docDir is null
            ? location
            : Path.Combine(docDir, location);

        if (!File.Exists(full)) return null;

        try { return EditorXmlSchemaValidator.LoadSchema(full); }
        catch (XmlSchemaException) { return null; }
        catch (System.Xml.XmlException) { return null; }
        catch (IOException) { return null; }
    }
}
