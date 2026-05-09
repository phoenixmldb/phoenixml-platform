using System.Collections.Generic;
using PhoenixmlDb.Core;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Find;
using Phoenixml.Platform.Editor.Xml.Tree;

namespace Phoenixml.Editor.Find;

/// <summary>
/// Surface that <see cref="FindController"/> depends on for document access,
/// tree navigation, and UI updates. Implemented by <c>EditorMainWindow</c>;
/// stubbed in unit tests.
/// </summary>
internal interface IFindHost
{
    /// <summary>The currently active XML document, or <see langword="null"/> when none is open.</summary>
    XmlDocument? ActiveDocument { get; }

    /// <summary>
    /// The tree-node provider for the active document, or <see langword="null"/>.
    /// Used to call <see cref="XmlTreeNodeProvider.TriggerRefresh"/> after index updates.
    /// </summary>
    XmlTreeNodeProvider? ActiveProvider { get; }

    /// <summary>
    /// Expands ancestors of <paramref name="nodeId"/> and selects the corresponding row.
    /// </summary>
    /// <param name="nodeId">String representation of the target <see cref="NodeId"/>.</param>
    void EnsureVisibleAndSelect(string nodeId);

    /// <summary>Hides and resets the find bar.</summary>
    void HideFindBar();

    /// <summary>Pushes a new match count to the find bar's counter display.</summary>
    /// <param name="active">1-based active match index (0 when no active match).</param>
    /// <param name="total">Total number of matches.</param>
    void SetMatchCount(int active, int total);
}

/// <summary>
/// Orchestrates the find bar, find service, match index, and tree navigation.
/// Owns the cycling state (the ordered match list and the current active index).
/// </summary>
/// <remarks>
/// Routing summary:
/// <list type="bullet">
///   <item><see cref="OnQueryChanged"/> → <see cref="FindService.Find"/> → <see cref="MatchIndex.Reset"/> → refresh.</item>
///   <item><see cref="OnNext"/> / <see cref="OnPrevious"/> → advance the active index → refresh.</item>
///   <item><see cref="OnClose"/> → clear the index → hide the bar.</item>
/// </list>
/// </remarks>
internal sealed class FindController
{
    private readonly IFindHost _host;
    private readonly MatchIndex _index;
    private IReadOnlyList<NodeId> _matches = [];
    private int _activeIndex = -1;

    /// <summary>
    /// Initializes a new <see cref="FindController"/> with the given host and match index.
    /// </summary>
    /// <param name="host">The editor host. Must not be <see langword="null"/>.</param>
    /// <param name="index">The shared match index. Must not be <see langword="null"/>.</param>
    public FindController(IFindHost host, MatchIndex index)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(index);
        _host = host;
        _index = index;
    }

    /// <summary>
    /// Called when the user changes the query or the match-case toggle.
    /// Runs the find, resets the active index to the first match, refreshes the tree.
    /// </summary>
    /// <param name="query">The new search string.</param>
    /// <param name="matchCase"><see langword="true"/> for case-sensitive search.</param>
    public void OnQueryChanged(string query, bool matchCase)
    {
        if (_host.ActiveDocument is not XmlDocument doc)
        {
            _matches = [];
            _activeIndex = -1;
            _index.Reset([], null);
            _host.SetMatchCount(0, 0);
            return;
        }

        _matches = FindService.Find(doc, query, matchCase);
        _activeIndex = _matches.Count > 0 ? 0 : -1;
        UpdateIndex();
        ShowActiveInTree();
        _host.SetMatchCount(_activeIndex < 0 ? 0 : _activeIndex + 1, _matches.Count);
    }

    /// <summary>
    /// Advances to the next match, wrapping around from the last.
    /// No-op when there are no matches.
    /// </summary>
    public void OnNext()
    {
        if (_matches.Count == 0) return;
        _activeIndex = (_activeIndex + 1) % _matches.Count;
        UpdateIndex();
        ShowActiveInTree();
        _host.SetMatchCount(_activeIndex + 1, _matches.Count);
    }

    /// <summary>
    /// Retreats to the previous match, wrapping around from the first.
    /// No-op when there are no matches.
    /// </summary>
    public void OnPrevious()
    {
        if (_matches.Count == 0) return;
        _activeIndex = (_activeIndex - 1 + _matches.Count) % _matches.Count;
        UpdateIndex();
        ShowActiveInTree();
        _host.SetMatchCount(_activeIndex + 1, _matches.Count);
    }

    /// <summary>
    /// Clears all match state, triggers a tree refresh, and hides the find bar.
    /// </summary>
    public void OnClose()
    {
        _matches = [];
        _activeIndex = -1;
        _index.Reset([], null);
        _host.ActiveProvider?.TriggerRefresh();
        _host.HideFindBar();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void UpdateIndex()
    {
        NodeId? active = _activeIndex >= 0 && _activeIndex < _matches.Count
            ? _matches[_activeIndex]
            : null;
        _index.Reset(_matches, active);
        _host.ActiveProvider?.TriggerRefresh();
    }

    private void ShowActiveInTree()
    {
        if (_activeIndex < 0 || _activeIndex >= _matches.Count) return;
        _host.EnsureVisibleAndSelect(_matches[_activeIndex].ToString());
    }
}
