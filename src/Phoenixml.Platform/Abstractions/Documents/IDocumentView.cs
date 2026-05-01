namespace Phoenixml.Platform.Abstractions.Documents;

/// <summary>
/// A view bound to a document. The same document may have zero or more views. Views
/// observe the document's <see cref="IDocument.Changed"/> event and update themselves;
/// they never mutate the document directly — mutations flow through commands on the
/// undo stack.
/// </summary>
public interface IDocumentView
{
    /// <summary>Gets the document this view is bound to.</summary>
    IDocument Document { get; }

    /// <summary>Gets the view kind identifier (e.g. "tree", "text", "preview").</summary>
    string ViewKindId { get; }

    /// <summary>Activates this view, making it the focused view for the document.</summary>
    void Activate();

    /// <summary>Deactivates this view when focus moves elsewhere.</summary>
    void Deactivate();
}
