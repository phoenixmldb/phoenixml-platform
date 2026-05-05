using System.Collections.Generic;
using System.IO;
using Phoenixml.Platform.Abstractions.Documents;

namespace Phoenixml.Platform.Editor.Xml;

/// <summary>
/// <see cref="IDocumentProvider"/> for .xml, .xsd, .xsl, .xslt, .rng, and .sch files.
/// Constructed by the host's DI; registered with the document service so "Open file"
/// routes XML-shaped documents through this provider.
/// </summary>
public sealed class XmlDocumentProvider : IDocumentProvider
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xml", ".xsd", ".xsl", ".xslt", ".rng", ".sch"
    };

    /// <inheritdoc/>
    public DocumentKind Kind => DocumentKind.Xml;

    /// <inheritdoc/>
    public bool CanOpen(Uri source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.IsFile) return false;
        var ext = Path.GetExtension(source.LocalPath);
        return SupportedExtensions.Contains(ext);
    }

    /// <inheritdoc/>
    public async ValueTask<IDocument> OpenAsync(Uri source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.IsFile)
            throw new NotSupportedException("Only file:// URIs are supported in v0.1.");
        return await XmlDocument.LoadAsync(source.LocalPath, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask<IDocument> NewAsync(CancellationToken ct)
    {
        var emptyXml = "<root/>";
        var (result, nsTable) = XmlDocument.ParseXml(emptyXml);
        var doc = new XmlDocument(DocumentId.New(), source: null, "untitled.xml", result, nsTable);
        return new ValueTask<IDocument>(doc);
    }
}
