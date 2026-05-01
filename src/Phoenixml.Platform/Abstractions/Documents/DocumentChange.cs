namespace Phoenixml.Platform.Abstractions.Documents;

/// <summary>Notification that a document was edited. Views subscribe via <see cref="IDocument.Changed"/>.</summary>
public sealed record DocumentChange(
    DocumentId DocumentId,
    string Description,
    DocumentChangeKind Kind,
    DateTimeOffset Timestamp);

/// <summary>Classifies the nature of a <see cref="DocumentChange"/>.</summary>
public enum DocumentChangeKind
{
    /// <summary>A content edit was applied.</summary>
    Edit,
    /// <summary>An undo operation was applied.</summary>
    UndoApplied,
    /// <summary>A redo operation was applied.</summary>
    RedoApplied,
    /// <summary>The document was loaded from its source.</summary>
    Loaded,
    /// <summary>The document was saved to its source.</summary>
    Saved,
    /// <summary>The document was reverted to its last saved state.</summary>
    Reverted,
}
