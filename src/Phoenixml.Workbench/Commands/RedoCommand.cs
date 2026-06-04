using Phoenixml.Workbench.ViewModels;

namespace Phoenixml.Workbench.Commands;

/// <summary>
/// Re-applies the most recently undone mutation on the active document's undo stack.
/// No-op when there is no active document or when <see cref="Platform.Abstractions.Documents.IUndoStack.CanRedo"/> is <see langword="false"/>.
/// </summary>
public static class RedoCommand
{
    /// <summary>Executes the redo operation for the active document.</summary>
    /// <param name="state">The workbench state whose <see cref="EditorState.ActiveDocument"/> is consulted.</param>
    public static void Execute(EditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var doc = state.ActiveDocument;
        if (doc is null) return;
        if (!doc.Undo.CanRedo) return;

        doc.Undo.Redo();
    }
}
