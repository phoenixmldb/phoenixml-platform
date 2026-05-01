using Phoenixml.Platform.Abstractions.Documents;

namespace Phoenixml.Platform.Documents;

#pragma warning disable CA1711 // Identifiers should not have incorrect suffix — 'Stack' is intentional; this IS an undo stack
internal sealed class UndoStack : IUndoStack
#pragma warning restore CA1711
{
    private readonly Document _document;
    private readonly Stack<IEditorCommand> _undo = new();
    private readonly Stack<IEditorCommand> _redo = new();

    public UndoStack(Document document) => _document = document;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? NextUndoDescription => _undo.TryPeek(out var c) ? c.Description : null;
    public string? NextRedoDescription => _redo.TryPeek(out var c) ? c.Description : null;

    public void Execute(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Try to merge with the most recent command (e.g. consecutive keystrokes collapse
        // into one undo step) before pushing. Merge succeeds → replace top of stack.
        if (_undo.TryPeek(out var previous) && previous.TryMergeWith(command, out var merged))
        {
            _undo.Pop();
            _undo.Push(merged);
            // Re-apply only the new command; the previous one was already applied.
            command.Apply(_document);
        }
        else
        {
            command.Apply(_document);
            _undo.Push(command);
        }
        _redo.Clear();
        _document.RaiseChange(command.Description, DocumentChangeKind.Edit);
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var command = _undo.Pop();
        command.Revert(_document);
        _redo.Push(command);
        _document.RaiseChange(command.Description, DocumentChangeKind.UndoApplied);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var command = _redo.Pop();
        command.Apply(_document);
        _undo.Push(command);
        _document.RaiseChange(command.Description, DocumentChangeKind.RedoApplied);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
