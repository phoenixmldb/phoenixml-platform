using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Documents;

namespace Phoenixml.Platform.Editor.Text;

/// <summary>
/// Plain-text document model used for files the editor doesn't parse natively
/// (currently <c>.xq</c> / <c>.xsl</c>). Maintains the source as a single string +
/// a monotonically increasing version number suitable for LSP <c>textDocument/didChange</c>.
/// </summary>
public sealed class TextDocument : Document
{
    private string _text;
    private int _version;

    /// <summary>Initializes a new <see cref="TextDocument"/>.</summary>
    public TextDocument(DocumentId id, Uri? source, string displayName, DocumentKind kind, string text)
        : base(id, source, displayName, kind)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        _version = 1;
    }

    /// <summary>Current buffer text.</summary>
    public string Text => _text;

    /// <summary>Monotonic version, incremented on every edit. Used for LSP didChange.</summary>
    public int Version => _version;

    /// <summary>
    /// Replaces the entire buffer with <paramref name="newText"/>. Increments
    /// <see cref="Version"/> and marks the document dirty. Raises <see cref="Document.Changed"/>.
    /// </summary>
    public void ReplaceAll(string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        if (string.Equals(_text, newText, StringComparison.Ordinal)) return;
        _text = newText;
        _version++;
        RaiseChange("text edit", DocumentChangeKind.Edit);
    }

    /// <inheritdoc/>
    public override async ValueTask SaveAsync(CancellationToken ct)
    {
        if (Source is null)
            throw new InvalidOperationException("Cannot save a document with no Source URI; use SaveAsAsync.");
        var path = Source.LocalPath;
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, _text, ct).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
        MarkClean();
    }

    /// <summary>Loads a <see cref="TextDocument"/> from a file path.</summary>
    public static async ValueTask<TextDocument> LoadAsync(string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);
        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var displayName = Path.GetFileName(path);
        var kind = KindFromExtension(path);
        return new TextDocument(DocumentId.New(), new Uri(path), displayName, kind, text);
    }

    /// <summary>Picks <see cref="DocumentKind"/> from the file extension.</summary>
    public static DocumentKind KindFromExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".xq", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".xqy", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".xquery", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".xqm", StringComparison.OrdinalIgnoreCase)
            ? DocumentKind.XQuery
            : ext.Equals(".xsl", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".xslt", StringComparison.OrdinalIgnoreCase)
                ? DocumentKind.Xslt
                : DocumentKind.Unknown;
    }
}
