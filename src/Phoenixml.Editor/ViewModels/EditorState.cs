using System;
using System.Collections.ObjectModel;
using Phoenixml.Platform.Documents;
using Phoenixml.Platform.Editor.Xml;

namespace Phoenixml.Editor.ViewModels;

/// <summary>
/// View-model state for the editor window. Holds an ordered list of open
/// <see cref="DocumentTab"/> instances with one designated as active.
/// <para>
/// Plan 26: generalized from <see cref="XmlDocument"/> to <see cref="Document"/> so
/// text-vocabulary documents (<c>TextDocument</c>) can ride the same tab system.
/// XML-only consumers type-narrow with <c>is XmlDocument</c>.
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
    /// Convenience accessor for the active tab's document. Setter dedupes: if a tab
    /// already holds the document it's activated; otherwise opens a new tab.
    /// </summary>
    public Document? ActiveDocument
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
    /// Adds a new tab for <paramref name="document"/> and activates it. If a tab
    /// already wraps the document, that tab is activated.
    /// </summary>
    public DocumentTab OpenDocument(Document document)
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
        TabAdded?.Invoke(newTab);
        ActiveTab = newTab;
        return newTab;
    }

    /// <summary>
    /// Closes <paramref name="tab"/>. Right-neighbor preferred for next-active; falls
    /// back to left neighbor when removing the last tab. <see cref="ActiveTab"/>
    /// becomes null when no tabs remain. No-op for unknown tabs.
    /// </summary>
    public void CloseTab(DocumentTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        var idx = _tabs.IndexOf(tab);
        if (idx < 0) return;

        var wasActive = ReferenceEquals(_activeTab, tab);
        _tabs.RemoveAt(idx);
        TabClosed?.Invoke(tab);

        if (!wasActive) return;

        if (_tabs.Count == 0)
        {
            ActiveTab = null;
            return;
        }

        var nextIdx = idx < _tabs.Count ? idx : _tabs.Count - 1;
        ActiveTab = _tabs[nextIdx];
    }

    /// <summary>Raised after <see cref="ActiveTab"/> changes (including to null).</summary>
    public event Action<DocumentTab?>? ActiveTabChanged;

    /// <summary>Raised after a tab is added via <see cref="OpenDocument"/>.</summary>
    public event Action<DocumentTab>? TabAdded;

    /// <summary>Raised after a tab is removed via <see cref="CloseTab"/>.</summary>
    public event Action<DocumentTab>? TabClosed;

    /// <summary>
    /// Swaps the document of the tab currently holding <paramref name="old"/> to
    /// <paramref name="fresh"/>. Fires <see cref="ActiveDocumentChanged"/> when the
    /// affected tab is the active one. No-op for unknown <paramref name="old"/>.
    /// </summary>
    public void ReplaceDocument(Document old, Document fresh)
    {
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(fresh);

        DocumentTab? affected = null;
        foreach (var tab in _tabs)
        {
            if (ReferenceEquals(tab.Document, old))
            {
                affected = tab;
                break;
            }
        }
        if (affected is null) return;

        affected.ReplaceDocument(fresh);

        if (ReferenceEquals(_activeTab, affected))
            ActiveDocumentChanged?.Invoke(fresh);
    }

    /// <summary>Raised after <see cref="ActiveDocument"/> changes.</summary>
    public event Action<Document?>? ActiveDocumentChanged;
}
