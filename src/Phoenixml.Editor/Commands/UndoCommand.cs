namespace Phoenixml.Editor.Commands;

/// <summary>
/// Reverts the most recent mutation on the active document's undo stack.
/// No-op when there is no active document or when <see cref="Platform.Abstractions.Documents.IUndoStack.CanUndo"/> is <see langword="false"/>.
/// </summary>
internal static class UndoCommand
{
    /// <summary>Executes the undo operation for the active document.</summary>
    /// <param name="window">The editor window whose <see cref="EditorMainWindow.State"/> is consulted.</param>
    public static void Execute(EditorMainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var doc = window.State.ActiveDocument;
        if (doc is null) return;
        if (!doc.Undo.CanUndo) return;

        doc.Undo.Undo();
    }
}
