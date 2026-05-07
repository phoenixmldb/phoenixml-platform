namespace Phoenixml.Editor.Commands;

/// <summary>
/// Re-applies the most recently undone mutation on the active document's undo stack.
/// No-op when there is no active document or when <see cref="Platform.Abstractions.Documents.IUndoStack.CanRedo"/> is <see langword="false"/>.
/// </summary>
internal static class RedoCommand
{
    /// <summary>Executes the redo operation for the active document.</summary>
    /// <param name="window">The editor window whose <see cref="EditorMainWindow.State"/> is consulted.</param>
    public static void Execute(EditorMainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var doc = window.State.ActiveDocument;
        if (doc is null) return;
        if (!doc.Undo.CanRedo) return;

        doc.Undo.Redo();
    }
}
