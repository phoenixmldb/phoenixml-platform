using PhoenixmlDb.Core;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Find;
using Phoenixml.Platform.Editor.Xml.Tree;

namespace Phoenixml.Editor.Find;

/// <summary>
/// Surface that <see cref="XPathQueryController"/> depends on. Mirrors <see cref="IFindHost"/>;
/// implemented by <c>EditorMainWindow</c> and stubbable in tests.
/// </summary>
internal interface IXPathQueryHost
{
    /// <summary>The active document, or <see langword="null"/> when none is open.</summary>
    XmlDocument? ActiveDocument { get; }

    /// <summary>Tree-node provider for the active document.</summary>
    XmlTreeNodeProvider? ActiveProvider { get; }

    /// <summary>Expands ancestors and selects the row for <paramref name="nodeId"/>.</summary>
    void EnsureVisibleAndSelect(string nodeId);

    /// <summary>Hides and resets the XPath bar.</summary>
    void HideXPathBar();

    /// <summary>Pushes a new match counter to the XPath bar.</summary>
    /// <param name="active">1-based active match index (0 when no active match).</param>
    /// <param name="total">Total match count.</param>
    /// <param name="error">Compile/runtime error, or <see langword="null"/> on success.</param>
    void SetXPathMatchCount(int active, int total, string? error);
}

/// <summary>
/// Orchestrates the XPath query bar, <see cref="XPathQueryService"/>, and <see cref="MatchIndex"/>.
/// Same shape as <see cref="FindController"/> but with one-shot evaluation per submit
/// (no debounce, no live re-query as the user types — XPath compile can be expensive).
/// </summary>
internal sealed class XPathQueryController
{
    private readonly IXPathQueryHost _host;
    private readonly MatchIndex _index;
    private IReadOnlyList<NodeId> _matches = [];
    private int _activeIndex = -1;
    private XmlDocument? _subscribedDoc;
    private string _lastQuery = string.Empty;
    private bool _refreshing;

    public XPathQueryController(IXPathQueryHost host, MatchIndex index)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(index);
        _host = host;
        _index = index;
    }

    /// <summary>
    /// Submits a new XPath expression. Evaluates against the active document, updates the match
    /// index, and navigates to the first match. Errors set the bar's error state and clear matches.
    /// </summary>
    public async Task OnQuerySubmittedAsync(string xpath)
    {
        // Re-submitting the same query advances to the next match (Enter-to-cycle pattern).
        // A new query resets to the first match.
        if (string.Equals(xpath, _lastQuery, StringComparison.Ordinal) && _matches.Count > 0)
        {
            OnNext();
            return;
        }

        _lastQuery = xpath;
        _activeIndex = 0;
        SubscribeToActiveDocument();
        await RefreshAsync().ConfigureAwait(false);
    }

    /// <summary>Advances to the next match, wrapping around.</summary>
    public void OnNext()
    {
        if (_matches.Count == 0) return;
        _activeIndex = (_activeIndex + 1) % _matches.Count;
        UpdateIndex();
        ShowActiveInTree();
        _host.SetXPathMatchCount(_activeIndex + 1, _matches.Count, error: null);
    }

    /// <summary>Retreats to the previous match, wrapping around.</summary>
    public void OnPrevious()
    {
        if (_matches.Count == 0) return;
        _activeIndex = (_activeIndex - 1 + _matches.Count) % _matches.Count;
        UpdateIndex();
        ShowActiveInTree();
        _host.SetXPathMatchCount(_activeIndex + 1, _matches.Count, error: null);
    }

    /// <summary>Clears all match state and hides the XPath bar.</summary>
    public void OnClose()
    {
        UnsubscribeFromActiveDocument();
        _matches = [];
        _activeIndex = -1;
        _lastQuery = string.Empty;
        _index.Reset([], null);
        _host.ActiveProvider?.TriggerRefresh();
        _host.HideXPathBar();
    }

    /// <summary>
    /// Clears match state without hiding the bar. Called by the editor when switching highlight
    /// modes (e.g. opening FindBar while XPathBar is also active) so stale tints don't linger.
    /// </summary>
    internal void ClearHighlights()
    {
        _matches = [];
        _activeIndex = -1;
        _index.Reset([], null);
        _host.ActiveProvider?.TriggerRefresh();
    }

    private async Task RefreshAsync()
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
                _host.SetXPathMatchCount(0, 0, error: null);
                return;
            }

            var result = await XPathQueryService.EvaluateAsync(doc, _lastQuery).ConfigureAwait(false);
            if (result.Error is not null)
            {
                _matches = [];
                _activeIndex = -1;
                UpdateIndex();
                _host.SetXPathMatchCount(0, 0, error: result.Error);
                return;
            }

            _matches = result.Nodes;
            _activeIndex = _matches.Count > 0
                ? Math.Min(_activeIndex >= 0 ? _activeIndex : 0, _matches.Count - 1)
                : -1;
            UpdateIndex();
            if (_activeIndex >= 0) ShowActiveInTree();
            _host.SetXPathMatchCount(_activeIndex < 0 ? 0 : _activeIndex + 1, _matches.Count, error: null);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void OnDocumentChanged(DocumentChange change)
    {
        _ = change;
        _ = RefreshAsync();
    }

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
