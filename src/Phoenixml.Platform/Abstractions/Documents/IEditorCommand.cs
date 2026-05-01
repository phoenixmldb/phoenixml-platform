namespace Phoenixml.Platform.Abstractions.Documents;

/// <summary>
/// A single, reversible edit. Commands compose (typing five characters merges into one
/// undo step), serialize (for crash recovery), and describe themselves (so the Edit menu
/// shows "Undo Rename element").
/// </summary>
public interface IEditorCommand
{
    /// <summary>Human-readable description of the edit, shown in Undo/Redo menu items.</summary>
    string Description { get; }

    /// <summary>Applies the edit to <paramref name="document"/>.</summary>
    void Apply(IDocument document);

    /// <summary>Reverts the edit on <paramref name="document"/>.</summary>
    void Revert(IDocument document);

    /// <summary>
    /// Attempts to merge this command with <paramref name="previous"/> into a single undo step.
    /// Returns <see langword="true"/> and sets <paramref name="merged"/> when the merge succeeds.
    /// </summary>
    bool TryMergeWith(IEditorCommand previous, out IEditorCommand merged);
}
