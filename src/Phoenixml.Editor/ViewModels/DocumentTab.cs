using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Editor.Xml;

namespace Phoenixml.Editor.ViewModels;

/// <summary>
/// One open document in the editor. Owns the <see cref="XmlDocument"/> and exposes
/// the title rendered on its tab header. Title updates fire <see cref="PropertyChanged"/>
/// so the tab strip re-renders without manual refresh.
/// </summary>
internal sealed class DocumentTab : INotifyPropertyChanged
{
    /// <summary>The document this tab wraps.</summary>
    public XmlDocument Document { get; private set; }

    /// <summary>
    /// Initializes a new <see cref="DocumentTab"/> for <paramref name="document"/>.
    /// Subscribes to the document's <c>Changed</c> event so <see cref="Title"/> fires
    /// <see cref="PropertyChanged"/> whenever dirty state or display name changes.
    /// </summary>
    public DocumentTab(XmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        Document.Changed += OnDocumentChanged;
    }

    /// <summary>
    /// Swaps the underlying document. Unsubscribes from the old document's Changed event
    /// and subscribes to the new one. Fires <see cref="PropertyChanged"/> for
    /// <see cref="Title"/> so any UI binding (e.g. a future tab strip) re-renders.
    /// No-op when <paramref name="fresh"/> is the same reference as the current document.
    /// </summary>
    /// <param name="fresh">The replacement document. Must not be <see langword="null"/>.</param>
    public void ReplaceDocument(XmlDocument fresh)
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
