using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Documents;
using Phoenixml.Platform.Editor.Xml;

namespace Phoenixml.Editor.ViewModels;

/// <summary>
/// One open document in the editor. Owns a <see cref="Document"/> (XML, text, etc.)
/// and exposes the title rendered on its tab header. Title updates fire
/// <see cref="PropertyChanged"/> so the tab strip re-renders without manual refresh.
/// </summary>
internal sealed class DocumentTab : INotifyPropertyChanged
{
    /// <summary>The document this tab wraps.</summary>
    public Document Document { get; private set; }

    /// <summary>
    /// Initializes a new <see cref="DocumentTab"/> for <paramref name="document"/>.
    /// Subscribes to the document's <c>Changed</c> event so <see cref="Title"/> fires
    /// <see cref="PropertyChanged"/> whenever dirty state or display name changes.
    /// </summary>
    public DocumentTab(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        Document.Changed += OnDocumentChanged;
    }

    /// <summary>
    /// Convenience: returns the document as <see cref="XmlDocument"/> when applicable.
    /// Most XML-only code paths use this to type-narrow.
    /// </summary>
    public bool TryGetXml(out XmlDocument? xml)
    {
        xml = Document as XmlDocument;
        return xml is not null;
    }

    /// <summary>
    /// Swaps the underlying document. Unsubscribes from the old document's Changed event
    /// and subscribes to the new one. Fires <see cref="PropertyChanged"/> for
    /// <see cref="Title"/> so any UI binding (e.g. a future tab strip) re-renders.
    /// No-op when <paramref name="fresh"/> is the same reference as the current document.
    /// </summary>
    public void ReplaceDocument(Document fresh)
    {
        ArgumentNullException.ThrowIfNull(fresh);
        if (ReferenceEquals(Document, fresh)) return;
        Document.Changed -= OnDocumentChanged;
        Document = fresh;
        Document.Changed += OnDocumentChanged;
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>
    /// Tab title — the document's display name plus a <c>*</c> suffix while it has
    /// unsaved changes (<c>IsDirty</c> is <see langword="true"/>).
    /// </summary>
    public string Title
    {
        get
        {
            var name = Document.DisplayName;
            return Document.IsDirty ? name + "*" : name;
        }
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnDocumentChanged(DocumentChange _) => OnPropertyChanged(nameof(Title));

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
