using Phoenixml.Platform.Editor.Xml;

namespace Phoenixml.Editor.ViewModels;

/// <summary>
/// View-model state for the editor window. Holds the active document and the
/// providers wired to the tree/text views. v0.1 supports one document at a time.
/// </summary>
internal sealed class EditorState
{
    private XmlDocument? _activeDocument;

    /// <summary>Gets or sets the currently open document. Setting fires <see cref="ActiveDocumentChanged"/>.</summary>
    public XmlDocument? ActiveDocument
    {
        get => _activeDocument;
        set
        {
            if (ReferenceEquals(_activeDocument, value)) return;
            _activeDocument = value;
            ActiveDocumentChanged?.Invoke(value);
        }
    }

    /// <summary>Raised when <see cref="ActiveDocument"/> changes.</summary>
    public event Action<XmlDocument?>? ActiveDocumentChanged;
}
