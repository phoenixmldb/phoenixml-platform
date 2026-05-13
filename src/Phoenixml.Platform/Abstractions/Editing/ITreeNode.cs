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

    /// <summary>
    /// Diff-highlight state for this node, set by the host's compare machinery (Plan 24).
    /// Returns <see cref="DiffHighlight.None"/> when no compare is active or this node
    /// has no diff entry. Diff tint takes precedence over <see cref="MatchHighlight"/>.
    /// Default implementation returns <see cref="DiffHighlight.None"/> so existing
    /// providers don't need to be updated until they participate in compare.
    /// </summary>
    DiffHighlight DiffHighlight => DiffHighlight.None;

    /// <summary>
    /// Optional documentation string (e.g. from <c>xs:annotation/xs:documentation</c> when
    /// the document is bound to a schema). Used by the host to render a tooltip on the row.
    /// Default implementation returns <see langword="null"/>.
    /// </summary>
    string? Documentation => null;
}

/// <summary>
/// Diff-highlight state for a single tree row, set by the host's compare machinery.
/// Mirrors <see cref="MatchHighlight"/> but for the dual-pane compare view (Plan 24).
/// </summary>
public enum DiffHighlight
{
    /// <summary>No compare is active, or this node has no diff entry.</summary>
    None,

    /// <summary>Node present only in the right (target) document.</summary>
    Added,

    /// <summary>Node present only in the left (source) document.</summary>
    Removed,

    /// <summary>Node value differs between left and right.</summary>
    Changed,
}
