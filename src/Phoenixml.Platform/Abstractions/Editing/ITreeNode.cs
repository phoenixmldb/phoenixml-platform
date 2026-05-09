namespace Phoenixml.Platform.Abstractions.Editing;

/// <summary>
/// Match-highlight state for a single tree row, set by the host's find machinery.
/// </summary>
/// <remarks>
/// Three-state so the renderer can distinguish "one of N matches" (subtle yellow tint)
/// from "the currently navigated match" (active blue). Introduced in Plan 15.
/// </remarks>
public enum MatchHighlight
{
    /// <summary>No find operation is active, or this node is not a match.</summary>
    None,

    /// <summary>This node is one of the current matches but is not the active (navigated) match.</summary>
    Match,

    /// <summary>This node is the currently active (navigated-to) match.</summary>
    ActiveMatch,
}

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

    /// <summary>
    /// Display value for this node (e.g. attribute value, text-only element's text content).
    /// Empty string for container nodes (elements with element children) and the document node itself.
    /// Vocabulary providers decide what counts as "the value" for each node kind.
    /// </summary>
    string Value { get; }

    /// <summary>
    /// Text shown in the inline edit TextBox when the user enters rename mode.
    /// For most nodes this matches <see cref="Label"/>, but vocabulary providers can return
    /// a different form for cells whose label includes presentation prefixes — for example,
    /// XML attributes whose label is <c>@id</c> return the bare local name <c>id</c> here so
    /// the user doesn't end up with <c>@@id</c> after editing.
    /// </summary>
    string EditableName { get; }

    /// <summary>
    /// Match-highlight state for this node, set by the host's find machinery.
    /// Returns <see cref="MatchHighlight.None"/> when no find operation is active or
    /// this node is not a match.
    /// </summary>
    MatchHighlight MatchHighlight { get; }
}
