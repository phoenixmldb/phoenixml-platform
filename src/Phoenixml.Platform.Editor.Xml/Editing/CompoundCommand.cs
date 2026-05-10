using PhoenixmlDb.Core;
using Phoenixml.Platform.Abstractions.Documents;
using PlatformDocument = Phoenixml.Platform.Abstractions.Documents.IDocument;
using PlatformEditorCommand = Phoenixml.Platform.Abstractions.Documents.IEditorCommand;

namespace Phoenixml.Platform.Editor.Xml.Editing;

/// <summary>
/// Wraps an ordered list of <see cref="IEditorCommand"/> instances and presents them as a single,
/// atomic undo/redo step. <see cref="Apply"/> runs children in forward order;
/// <see cref="Revert"/> runs them in reverse order.
/// </summary>
/// <remarks>
/// Use <see cref="CompoundCommand"/> whenever a user gesture must mutate the document in
/// multiple vocabulary steps that belong to one conceptual action — for example,
/// "Replace All" (N rename/set-value commands) or a future drag-reorder (delete + insert).
/// Because the compound is pushed as one item onto the undo stack, a single Ctrl+Z undoes
/// the entire batch.
/// </remarks>
public sealed class CompoundCommand : PlatformEditorCommand
{
    private readonly IReadOnlyList<PlatformEditorCommand> _commands;
    private readonly string _description;

    /// <summary>
    /// Initializes a new <see cref="CompoundCommand"/>.
    /// </summary>
    /// <param name="commands">
    /// The ordered list of child commands. Must not be <see langword="null"/> and must contain
    /// at least one command.
    /// </param>
    /// <param name="description">
    /// A human-readable description of the batch, shown in the undo history.
    /// Must not be <see langword="null"/> or empty.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="commands"/> or <paramref name="description"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="description"/> is empty, or when <paramref name="commands"/> contains
    /// no elements.
    /// </exception>
    public CompoundCommand(IReadOnlyList<PlatformEditorCommand> commands, string description)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentException.ThrowIfNullOrEmpty(description);
        if (commands.Count == 0)
            throw new ArgumentException("CompoundCommand requires at least one child command.", nameof(commands));
        _commands = commands;
        _description = description;
    }

    /// <inheritdoc/>
    public string Description => _description;

    /// <inheritdoc/>
    public IEnumerable<NodeId> ReferencedNodeIds =>
        _commands.SelectMany(c => c.ReferencedNodeIds);

    /// <inheritdoc/>
    /// <remarks>
    /// Applies each child command in the order they were provided to the constructor.
    /// </remarks>
    public void Apply(PlatformDocument document)
    {
        foreach (var cmd in _commands)
            cmd.Apply(document);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reverts each child command in reverse order relative to <see cref="Apply"/>.
    /// This ensures that mutations which depend on earlier mutations are undone correctly.
    /// </remarks>
    public void Revert(PlatformDocument document)
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
            _commands[i].Revert(document);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Compound commands never merge with other commands. Each compound represents a
    /// deliberate user gesture (e.g., Replace All) and must remain a distinct undo step.
    /// </remarks>
    public bool TryMergeWith(PlatformEditorCommand previous, out PlatformEditorCommand merged)
    {
        merged = this;
        return false;
    }
}
