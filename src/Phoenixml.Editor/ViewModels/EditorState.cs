using System;
using System.Collections.ObjectModel;
using Phoenixml.Platform.Editor.Xml;

namespace Phoenixml.Editor.ViewModels;

/// <summary>
/// View-model state for the editor window. Holds an ordered list of open
/// <see cref="DocumentTab"/> instances with one designated as active.
/// <para>
/// <see cref="ActiveDocument"/> is preserved as a forwarding accessor so existing
/// callers (Save command, Insert/Delete handlers, address bar, status bar) compile
/// unchanged. Its setter routes through <see cref="OpenDocument"/> so the same
/// document is never opened in two tabs.
/// </para>
/// </summary>
internal sealed class EditorState
{
    private readonly ObservableCollection<DocumentTab> _tabs = new();
    private DocumentTab? _activeTab;

    /// <summary>Initializes a new <see cref="EditorState"/> with an empty tab list.</summary>
    public EditorState()
    {
        Tabs = new ReadOnlyObservableCollection<DocumentTab>(_tabs);
    }

    /// <summary>
    /// The ordered list of open tabs. Bind <c>ItemsSource</c> to this; mutate only
    /// via <see cref="OpenDocument"/> and <see cref="CloseTab"/>.
    /// </summary>
    public ReadOnlyObservableCollection<DocumentTab> Tabs { get; }

    /// <summary>
    /// The currently focused tab, or <see langword="null"/> when no tab is open.
    /// Setting to a tab not in <see cref="Tabs"/> throws <see cref="ArgumentException"/>.
    /// </summary>
    public DocumentTab? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (ReferenceEquals(_activeTab, value)) return;
            if (value is not null && !_tabs.Contains(value))
                throw new ArgumentException("Tab is not in the open list.", nameof(value));
            _activeTab = value;
            ActiveTabChanged?.Invoke(value);
            ActiveDocumentChanged?.Invoke(value?.Document);
        }
    }

    /// <summary>
    /// Convenience accessor for the active tab's document. Preserved for source
    /// compatibility — callers should prefer <see cref="OpenDocument"/> for new code.
    /// <para>
    /// The setter deduplicates by document reference: if the document already has a
    /// tab it is activated; otherwise <see cref="OpenDocument"/> is called to add one.
    /// Setting <see langword="null"/> clears the active tab.
    /// </para>
    /// </summary>
    public XmlDocument? ActiveDocument
    {
        get => _activeTab?.Document;
        set
        {
            if (value is null)
            {
                ActiveTab = null;
                return;
            }

            foreach (var tab in _tabs)
            {
                if (ReferenceEquals(tab.Document, value))
                {
                    ActiveTab = tab;
                    return;
                }
            }

            OpenDocument(value);
        }
    }

    /// <summary>
    /// Adds a new tab for <paramref name="document"/> and activates it. If
    /// <paramref name="document"/> already has a tab that tab is activated instead
    /// (no duplicates). Returns the tab — new or existing.
    /// </summary>
    public DocumentTab OpenDocument(XmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var tab in _tabs)
        {
            if (ReferenceEquals(tab.Document, document))
            {
                ActiveTab = tab;
                return tab;
            }
        }

        var newTab = new DocumentTab(document);
        _tabs.Add(newTab);
        ActiveTab = newTab;
        return newTab;
    }

    /// <summary>
    /// Closes <paramref name="tab"/>. If it was the active tab a neighbor is
    /// activated: the tab to the right is preferred; the tab to the left is used
    /// when closing the rightmost tab. If no tabs remain <see cref="ActiveTab"/>
    /// becomes <see langword="null"/>. No-ops if <paramref name="tab"/> is not open.
    /// </summary>
    public void CloseTab(DocumentTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        var idx = _tabs.IndexOf(tab);
        if (idx < 0) return;

        var wasActive = ReferenceEquals(_activeTab, tab);
        _tabs.RemoveAt(idx);

        if (!wasActive) return;

        if (_tabs.Count == 0)
        {
            ActiveTab = null;
            return;
        }

        // Prefer right neighbor; fall back to left when we removed the last item.
        var nextIdx = idx < _tabs.Count ? idx : _tabs.Count - 1;
        ActiveTab = _tabs[nextIdx];
    }

    /// <summary>Raised after <see cref="ActiveTab"/> changes (including to <see langword="null"/>).</summary>
    public event Action<DocumentTab?>? ActiveTabChanged;

    /// <summary>
    /// Raised after <see cref="ActiveDocument"/> changes. Preserved for backwards
    /// compatibility — fires alongside <see cref="ActiveTabChanged"/>.
    /// </summary>
    public event Action<XmlDocument?>? ActiveDocumentChanged;
}
