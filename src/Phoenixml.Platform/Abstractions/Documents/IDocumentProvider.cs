namespace Phoenixml.Platform.Abstractions.Documents;

/// <summary>Opens or creates documents of a specific <see cref="DocumentKind"/>.</summary>
public interface IDocumentProvider
{
    /// <summary>Gets the vocabulary kind this provider handles.</summary>
    DocumentKind Kind { get; }

    /// <summary>Returns <see langword="true"/> if this provider can open the given <paramref name="source"/> URI.</summary>
    bool CanOpen(Uri source);

    /// <summary>Opens the document at <paramref name="source"/>.</summary>
    ValueTask<IDocument> OpenAsync(Uri source, CancellationToken ct);

    /// <summary>Creates a new, empty document.</summary>
    ValueTask<IDocument> NewAsync(CancellationToken ct);
}
