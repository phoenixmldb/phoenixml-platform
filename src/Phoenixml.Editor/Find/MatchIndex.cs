using System.Collections.Generic;
using PhoenixmlDb.Core;
using Phoenixml.Platform.Abstractions.Editing;

namespace Phoenixml.Editor.Find;

/// <summary>
/// Tracks the current set of matching <see cref="NodeId"/>s and the single active (focused)
/// match produced by the most recent <c>FindService</c> run. Mirrors the Plan 13
/// <c>DiagnosticIndex</c> pattern; consumed by
/// <see cref="Phoenixml.Platform.Editor.Xml.Tree.XmlTreeNodeProvider"/> via
/// <see cref="IMatchIndex"/> to annotate tree rows with match-highlight state.
/// </summary>
internal sealed class MatchIndex : IMatchIndex
{
    private HashSet<NodeId> _matches = [];
    private NodeId? _active;

    /// <summary>All NodeIds that are currently matched.</summary>
    public IReadOnlyCollection<NodeId> Matches => _matches;

    /// <summary>The currently active (selected/navigated-to) match, or <see langword="null"/> when there is none.</summary>
    public NodeId? Active => _active;

    /// <summary>
    /// Replaces the current match set and active match with a new result.
    /// </summary>
    /// <param name="matches">
    /// The new list of matching NodeIds. Must not be <see langword="null"/>.
    /// Pass an empty list to clear all matches.
    /// </param>
    /// <param name="active">The active match, or <see langword="null"/> for no active match.</param>
    public void Reset(IReadOnlyList<NodeId> matches, NodeId? active)
    {
        ArgumentNullException.ThrowIfNull(matches);
        _matches = new HashSet<NodeId>(matches);
        _active = active;
    }

    /// <inheritdoc/>
    public bool IsMatch(NodeId id) => _matches.Contains(id);

    /// <inheritdoc/>
    public bool IsActive(NodeId id) => _active is NodeId a && a == id;
}
