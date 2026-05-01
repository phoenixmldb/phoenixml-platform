namespace Phoenixml.Platform.Abstractions.Documents;

/// <summary>Stable identifier for a document, preserved across save/reload cycles.</summary>
public readonly record struct DocumentId(Guid Value)
{
    /// <summary>Creates a new unique <see cref="DocumentId"/>.</summary>
    public static DocumentId New() => new(Guid.NewGuid());

    /// <inheritdoc/>
    public override string ToString() => Value.ToString("D");
}
