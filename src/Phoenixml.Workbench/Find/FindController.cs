using System.Collections.Generic;
using PhoenixmlDb.Core;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Editing;
using Phoenixml.Platform.Editor.Xml.Find;
using Phoenixml.Platform.Editor.Xml.Tree;

namespace Phoenixml.Workbench.Find;

/// <summary>
/// Surface that <see cref="FindController"/> depends on for document access,
/// tree navigation, and UI updates. Implemented by <c>EditorMainWindow</c>;
/// stubbed in unit tests.
/// </summary>
public interface IFindHost
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
public sealed class FindController
{
    private readonly IFindHost _host;
    private readonly MatchIndex _index;
    private IReadOnlyList<NodeId> _matches = [];
    private int _activeIndex = -1;
    private string _lastQuery = string.Empty;
    private bool _lastMatchCase;
    private XmlDocument? _subscribedDoc;
    private bool _refreshing;

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
    /// Captures the new query state, subscribes to document changes, and refreshes matches.
    /// </summary>
    /// <param name="query">The new search string.</param>
    /// <param name="matchCase"><see langword="true"/> for case-sensitive search.</param>
    public void OnQueryChanged(string query, bool matchCase)
    {
        _lastQuery = query;
        _lastMatchCase = matchCase;
        // Reset active index to first match when the query changes.
        _activeIndex = 0;
        SubscribeToActiveDocument();
        RefreshMatches();
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
    /// Unsubscribes from the active document's <c>Changed</c> event.
    /// </summary>
    public void OnClose()
    {
        UnsubscribeFromActiveDocument();
        _matches = [];
        _activeIndex = -1;
        _lastQuery = string.Empty;
        _index.Reset([], null);
        _host.ActiveProvider?.TriggerRefresh();
        _host.HideFindBar();
    }

    /// <summary>
    /// Replaces the currently active match with <paramref name="replacement"/>.
    /// Builds a command via <see cref="ReplaceService"/> and pushes it to the document's
    /// undo stack. A subsequent <c>Document.Changed</c> fires and re-runs
    /// <see cref="RefreshMatches"/> automatically.
    /// </summary>
    /// <param name="replacement">The replacement string. Must not be <see langword="null"/>.</param>
    public void OnReplaceCurrent(string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (_host.ActiveDocument is not XmlDocument doc) return;
        if (_activeIndex < 0 || _activeIndex >= _matches.Count) return;

        var single = new[] { _matches[_activeIndex] };
        var commands = ReplaceService.BuildReplacements(doc, single, _lastQuery, replacement, _lastMatchCase);
        if (commands.Count == 0) return;

        IEditorCommand cmd = commands.Count == 1
            ? commands[0]
            : new CompoundCommand(commands, "Replace");
        doc.Undo.Execute(cmd);
        // Document.Changed fires → OnDocumentChanged → RefreshMatches.
    }

    /// <summary>
    /// Replaces all current matches with <paramref name="replacement"/>.
    /// Wraps all edits in a <see cref="CompoundCommand"/> so the entire Replace All operation
    /// is a single undo step.
    /// </summary>
    /// <param name="replacement">The replacement string. Must not be <see langword="null"/>.</param>
    public void OnReplaceAll(string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (_host.ActiveDocument is not XmlDocument doc) return;
        if (_matches.Count == 0) return;

        var commands = ReplaceService.BuildReplacements(doc, _matches, _lastQuery, replacement, _lastMatchCase);
        if (commands.Count == 0) return;

        var compound = new CompoundCommand(commands, $"Replace all '{_lastQuery}' with '{replacement}'");
        doc.Undo.Execute(compound);
        // Document.Changed fires → OnDocumentChanged → RefreshMatches.
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void RefreshMatches()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            if (_host.ActiveDocument is not XmlDocument doc)
            {
                _matches = [];
                _activeIndex = -1;
                UpdateIndex();
                _host.SetMatchCount(0, 0);
                return;
            }

            _matches = FindService.Find(doc, _lastQuery, _lastMatchCase);
            _activeIndex = _matches.Count > 0
                ? Math.Min(_activeIndex >= 0 ? _activeIndex : 0, _matches.Count - 1)
                : -1;
            UpdateIndex();
            if (_activeIndex >= 0) ShowActiveInTree();
            _host.SetMatchCount(_activeIndex < 0 ? 0 : _activeIndex + 1, _matches.Count);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void OnDocumentChanged(DocumentChange _) => RefreshMatches();

    private void SubscribeToActiveDocument()
    {
        var doc = _host.ActiveDocument;
        if (ReferenceEquals(doc, _subscribedDoc)) return;
        if (_subscribedDoc is not null) _subscribedDoc.Changed -= OnDocumentChanged;
        _subscribedDoc = doc;
        if (_subscribedDoc is not null) _subscribedDoc.Changed += OnDocumentChanged;
    }

    private void UnsubscribeFromActiveDocument()
    {
        if (_subscribedDoc is not null) _subscribedDoc.Changed -= OnDocumentChanged;
        _subscribedDoc = null;
    }

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
