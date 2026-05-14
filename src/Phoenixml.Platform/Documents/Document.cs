using Phoenixml.Platform.Abstractions.Documents;

namespace Phoenixml.Platform.Documents;

/// <summary>
/// Abstract base class for all documents in the Phoenixml document model.
/// Concrete documents (e.g. <c>XmlDocument</c>) derive from this and implement
/// <see cref="SaveAsync"/>.
/// </summary>
public abstract class Document : IDocument
{
    private readonly UndoStack _undo;
    private bool _isDirty;

    /// <summary>
    /// Initializes a new <see cref="Document"/> instance.
    /// </summary>
    /// <param name="id">The stable document identifier.</param>
    /// <param name="source">The URI of the document's source, or <see langword="null"/> for new unsaved documents.</param>
    /// <param name="displayName">The display name shown in tabs and title bars.</param>
    /// <param name="kind">The vocabulary kind of this document.</param>
    protected Document(DocumentId id, Uri? source, string displayName, DocumentKind kind)
    {
        Id = id;
        Source = source;
        DisplayName = displayName;
        Kind = kind;
        _undo = new UndoStack(this);
    }

    /// <inheritdoc/>
    public DocumentId Id { get; }

    /// <inheritdoc/>
    public Uri? Source { get; protected set; }

    /// <inheritdoc/>
    public string DisplayName { get; protected set; }

    /// <inheritdoc/>
    public DocumentKind Kind { get; }

    /// <inheritdoc/>
    public bool IsDirty
    {
        get => _isDirty;
        protected set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            // Note: dirty changes don't raise a DocumentChange — that's reserved for content edits.
        }
    }

    /// <inheritdoc/>
    public IUndoStack Undo => _undo;

    /// <inheritdoc/>
#pragma warning disable CA1003 // Use generic event handler instances — intentional Action<T> design per spec §3.1
    public event Action<DocumentChange>? Changed;
#pragma warning restore CA1003

    /// <inheritdoc/>
    public abstract ValueTask SaveAsync(CancellationToken ct);

    /// <inheritdoc/>
    public virtual ValueTask<bool> CanCloseAsync(CancellationToken ct) => ValueTask.FromResult(true);

    /// <summary>Raises <see cref="Changed"/>. Called from the undo stack after Apply/Revert.</summary>
    /// <remarks>
    /// Protected so vocabulary types in sibling assemblies (e.g. <c>TextDocument</c>) can
    /// fire change notifications when their edit path doesn't go through the undo stack.
    /// </remarks>
#pragma warning disable CA1030 // CA1030 suggests events; existing UndoStack-driven design uses a direct invoke pattern
    protected internal void RaiseChange(string description, DocumentChangeKind kind)
#pragma warning restore CA1030
    {
        IsDirty = true;
        Changed?.Invoke(new DocumentChange(Id, description, kind, DateTimeOffset.UtcNow));
    }

    /// <summary>Resets dirty state after a successful save.</summary>
    protected void MarkClean()
    {
        IsDirty = false;
        Changed?.Invoke(new DocumentChange(Id, "Saved", DocumentChangeKind.Saved, DateTimeOffset.UtcNow));
    }
}
