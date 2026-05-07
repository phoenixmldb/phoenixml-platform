using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    public XmlDocument Document { get; }

    /// <summary>
    /// Initializes a new <see cref="DocumentTab"/> for <paramref name="document"/>.
    /// Subscribes to <see cref="XmlDocument.Changed"/> so <see cref="Title"/> fires
    /// <see cref="PropertyChanged"/> whenever dirty state or display name changes.
    /// </summary>
    public DocumentTab(XmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        Document.Changed += _ => OnPropertyChanged(nameof(Title));
    }

    /// <summary>
    /// Tab title — the document's display name plus a <c>*</c> suffix while it has
    /// unsaved changes (<see cref="XmlDocument.IsDirty"/> is <see langword="true"/>).
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

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
