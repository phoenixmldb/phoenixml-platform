using Phoenixml.Platform.Abstractions.Documents;

namespace Phoenixml.Platform.Documents;

/// <summary>
/// Tracks open documents, surfaces "active document" state, and dispatches
/// close-with-confirm. All mutation flows through the public methods; views
/// observe via the events.
/// </summary>
public sealed class DocumentService
{
    private readonly List<IDocument> _open = new();

    /// <summary>Gets the list of currently open documents.</summary>
    public IReadOnlyList<IDocument> OpenDocuments => _open;

    /// <summary>Gets the currently active document, or <see langword="null"/> if no document is open.</summary>
    public IDocument? ActiveDocument { get; private set; }

    /// <summary>Raised when a document is added to the open list.</summary>
#pragma warning disable CA1003 // Use generic event handler instances — intentional Action<T> pattern
    public event Action<IDocument>? DocumentOpened;

    /// <summary>Raised when a document is removed from the open list.</summary>
    public event Action<IDocument>? DocumentClosed;

    /// <summary>Raised when the active document changes.</summary>
    public event Action<IDocument?>? ActiveDocumentChanged;
#pragma warning restore CA1003

    /// <summary>
    /// Adds <paramref name="document"/> to the open list and makes it the active document.
    /// </summary>
    public void Add(IDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _open.Add(document);
        DocumentOpened?.Invoke(document);
        SetActive(document);
    }

    /// <summary>
    /// Closes <paramref name="document"/>, first asking <see cref="IDocument.CanCloseAsync"/> whether
    /// it is safe to do so. Returns <see langword="false"/> if the document declined to close.
    /// </summary>
    public async ValueTask<bool> CloseAsync(IDocument document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_open.Contains(document)) return true;
        if (!await document.CanCloseAsync(ct).ConfigureAwait(false)) return false;

        _open.Remove(document);
        DocumentClosed?.Invoke(document);
        if (ReferenceEquals(ActiveDocument, document))
            SetActive(_open.Count > 0 ? _open[^1] : null);
        return true;
    }

    /// <summary>
    /// Makes <paramref name="document"/> the active document.
    /// Passing <see langword="null"/> clears the active document.
    /// Throws <see cref="InvalidOperationException"/> if <paramref name="document"/> is not in the open list.
    /// </summary>
    public void SetActive(IDocument? document)
    {
        if (ReferenceEquals(ActiveDocument, document)) return;
        if (document is not null && !_open.Contains(document))
            throw new InvalidOperationException("Cannot activate a document that is not open.");
        ActiveDocument = document;
        ActiveDocumentChanged?.Invoke(document);
    }
}
