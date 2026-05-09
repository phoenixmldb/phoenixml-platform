namespace Phoenixml.Platform.Abstractions.Documents;

/// <summary>Command-pattern undo/redo stack for a document.</summary>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix — 'Stack' is intentional; this IS an undo stack
public interface IUndoStack
#pragma warning restore CA1711
{
    /// <summary>Gets a value indicating whether there is a command available to undo.</summary>
    bool CanUndo { get; }

    /// <summary>Gets a value indicating whether there is a command available to redo.</summary>
    bool CanRedo { get; }

    /// <summary>Gets the description of the next command to be undone, or <see langword="null"/> if the stack is empty.</summary>
    string? NextUndoDescription { get; }

    /// <summary>Gets the description of the next command to be redone, or <see langword="null"/> if the redo stack is empty.</summary>
    string? NextRedoDescription { get; }

    /// <summary>Applies <paramref name="command"/> and pushes it onto the undo stack.</summary>
    void Execute(IEditorCommand command);

    /// <summary>Reverts the most recent command and moves it to the redo stack.</summary>
    void Undo();

    /// <summary>Re-applies the most recently undone command and moves it back to the undo stack.</summary>
    void Redo();

    /// <summary>Empties both the undo and redo stacks.</summary>
    void Clear();

    /// <summary>
    /// Read-only view of all commands currently on the undo stack (most-recent-first).
    /// Compact-on-Save walks this to collect <see cref="IEditorCommand.ReferencedNodeIds"/>
    /// for commands that pin orphaned nodes (e.g. deleted subtrees).
    /// </summary>
    IReadOnlyCollection<IEditorCommand> UndoCommands { get; }

    /// <summary>
    /// Read-only view of all commands currently on the redo stack (most-recent-first).
    /// Compact-on-Save walks this alongside <see cref="UndoCommands"/> so that redo-able
    /// deletions also keep their orphaned subtrees alive in the resolver dictionary.
    /// </summary>
    IReadOnlyCollection<IEditorCommand> RedoCommands { get; }
}
