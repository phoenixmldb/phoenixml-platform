namespace Phoenixml.Platform.Abstractions.Documents;

/// <summary>The central contract for a document in the Phoenixml document model.</summary>
public interface IDocument
{
    /// <summary>Gets the stable identifier for this document.</summary>
    DocumentId Id { get; }

    /// <summary>Gets the URI of the document's source, or <see langword="null"/> for unsaved new documents.</summary>
    Uri? Source { get; }

    /// <summary>Gets the display name shown in tabs and title bars.</summary>
    string DisplayName { get; }

    /// <summary>Gets the vocabulary kind of this document.</summary>
    DocumentKind Kind { get; }

    /// <summary>Gets a value indicating whether the document has unsaved changes.</summary>
    bool IsDirty { get; }

    /// <summary>Gets the undo/redo stack for this document.</summary>
    IUndoStack Undo { get; }

    /// <summary>Raised after every applied edit. Plain Action&lt;T&gt; — zero System.Reactive dependency in Abstractions.</summary>
#pragma warning disable CA1003 // Use generic event handler instances — intentional; no EventArgs wrapper, no System.Reactive
    event Action<DocumentChange>? Changed;
#pragma warning restore CA1003

    /// <summary>Saves the document to its source.</summary>
    ValueTask SaveAsync(CancellationToken ct);

    /// <summary>Asks whether it is safe to close the document (e.g. prompt save if dirty).</summary>
    ValueTask<bool> CanCloseAsync(CancellationToken ct);
}
