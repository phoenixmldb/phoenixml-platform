namespace Phoenixml.Platform.Abstractions.Editing;

/// <summary>
/// A single node in a tree shown by <see cref="ITreeNodeProvider"/>. Vocabulary
/// providers (XML, XSLT, etc.) build their own concrete node types and surface
/// them through this contract so the generic tree control can render them.
/// </summary>
public interface ITreeNode
{
    /// <summary>Stable identifier used for selection persistence and equality.</summary>
    string Id { get; }

    /// <summary>Display label.</summary>
    string Label { get; }

    /// <summary>Optional icon key (theme-resolved); null = no icon.</summary>
    string? IconKey { get; }

    /// <summary>True if this node has children that haven't been enumerated yet.</summary>
    bool HasChildren { get; }
}
