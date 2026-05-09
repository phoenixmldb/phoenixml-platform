using PhoenixmlDb.Core;

namespace Phoenixml.Platform.Abstractions.Editing;

/// <summary>
/// Read-only view of the current find-match state. Consumed by tree-node providers to
/// annotate rows with match-highlight state without taking a hard dependency on the editor
/// assembly's concrete <c>MatchIndex</c> class.
/// </summary>
/// <remarks>
/// Mirrors <see cref="IDiagnosticIndex"/> from Plan 13. The editor-side
/// <c>Phoenixml.Editor.Find.MatchIndex</c> implements this interface so the XML vocabulary
/// provider can read it via <c>XmlTreeNodeProvider.MatchIndex</c>.
/// </remarks>
public interface IMatchIndex
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="id"/> is in the current match set.
    /// </summary>
    /// <param name="id">The XDM node id to look up.</param>
    bool IsMatch(NodeId id);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="id"/> is the currently active
    /// (navigated-to) match.
    /// </summary>
    /// <param name="id">The XDM node id to look up.</param>
    bool IsActive(NodeId id);
}
