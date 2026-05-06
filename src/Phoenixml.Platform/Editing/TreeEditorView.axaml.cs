using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Phoenixml.Platform.Abstractions.Editing;

namespace Phoenixml.Platform.Editing;

/// <summary>
/// A vocabulary-agnostic tree editor. Accepts an <see cref="ITreeNodeProvider"/> and renders
/// its node hierarchy using Avalonia's <c>TreeView</c> with a <c>HierarchicalDataTemplate</c>.
/// Vocabulary packages supply concrete providers; this control has no XML/XSLT knowledge.
/// </summary>
/// <remarks>
/// v0.3 ships a working tree. Virtualization, drag-and-drop, in-place rename, and accessibility
/// hints are deferred to a later iteration (Plan 6).
/// </remarks>
public sealed partial class TreeEditorView : UserControl
{
    /// <summary>Initializes a new instance of <see cref="TreeEditorView"/>.</summary>
    public TreeEditorView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private ITreeNodeProvider? _provider;

    /// <summary>Gets the currently active node provider.</summary>
    public ITreeNodeProvider? Provider => _provider;

    /// <summary>Gets the currently selected node, or <see langword="null"/> when nothing is selected.</summary>
    public ITreeNode? SelectedNode { get; private set; }

    /// <summary>Raised when the user changes the selection in the tree.</summary>
#pragma warning disable CA1003 // Use generic event handler instances — intentional; Action<T> pattern
    public event Action<ITreeNode>? SelectionChanged;

    /// <summary>Raised when the user double-clicks a node.</summary>
    public event Action<ITreeNode>? NodeActivated;
#pragma warning restore CA1003

    /// <summary>
    /// Replaces the active provider and rebuilds the tree. Pass <see langword="null"/> to clear.
    /// Subscribes to <see cref="ITreeNodeProvider.Changed"/> on the new provider.
    /// </summary>
    public void SetProvider(ITreeNodeProvider? provider)
    {
        if (_provider is not null)
            _provider.Changed -= OnProviderChanged;

        _provider = provider;

        if (_provider is not null)
            _provider.Changed += OnProviderChanged;

        Refresh();
    }

    /// <summary>
    /// Selects the node with the given <paramref name="id"/>, if it is among the roots
    /// (deep selection is deferred to a later iteration).
    /// </summary>
    public void SelectById(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var host = this.FindControl<TreeView>("TreeHost");
        if (host is null) return;

        // Search root-level items only (deep traversal deferred to Plan 6).
        foreach (var item in host.Items.OfType<TreeNodeViewModel>())
        {
            if (item.Node.Id == id)
            {
                host.SelectedItem = item;
                return;
            }
        }
    }

    private void Refresh()
    {
        var host = this.FindControl<TreeView>("TreeHost");
        if (host is null) return;

        if (_provider is null)
        {
            host.ItemsSource = null;
            return;
        }

        // Auto-expand the root level so users see the document's top-level structure
        // immediately on Open without hunting for the small expand chevron. Deeper nodes
        // are still click-to-expand. (One root node almost always has just one or two
        // children — expanding one more level too keeps the UX informative.)
        host.ItemsSource = _provider.Roots
            .Select(n =>
            {
                var vm = new TreeNodeViewModel(n, _provider) { IsExpanded = true };
                foreach (var child in vm.Children)
                    child.IsExpanded = true;
                return vm;
            })
            .ToList();
    }

    private void OnProviderChanged() => Refresh();

    private void TreeHost_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TreeView tv && tv.SelectedItem is TreeNodeViewModel row)
        {
            SelectedNode = row.Node;
            SelectionChanged?.Invoke(row.Node);
        }
    }

    private void TreeHost_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is TreeView tv && tv.SelectedItem is TreeNodeViewModel row)
            NodeActivated?.Invoke(row.Node);
    }
}

/// <summary>
/// View-model wrapper for a single tree node. Children are fetched lazily via the provider
/// when Avalonia first requests the <see cref="Children"/> collection.
/// </summary>
internal sealed class TreeNodeViewModel
{
    private readonly ITreeNodeProvider _provider;
    private IReadOnlyList<TreeNodeViewModel>? _children;

    /// <summary>The underlying domain node.</summary>
    public ITreeNode Node { get; }

    /// <summary>Display label (forwarded from <see cref="Node"/>).</summary>
    public string Label => Node.Label;

    /// <summary>Whether the corresponding TreeViewItem should render expanded.
    /// Bound two-way via the TreeView's IsExpanded style binding.</summary>
    public bool IsExpanded { get; set; }

    /// <summary>
    /// Lazily-loaded children. On first access, calls <see cref="ITreeNodeProvider.GetChildren"/>
    /// and wraps each child in a new <see cref="TreeNodeViewModel"/>.
    /// </summary>
    public IReadOnlyList<TreeNodeViewModel> Children
    {
        get
        {
            if (_children is not null) return _children;
            _children = _provider.GetChildren(Node)
                .Select(n => new TreeNodeViewModel(n, _provider))
                .ToList();
            return _children;
        }
    }

    internal TreeNodeViewModel(ITreeNode node, ITreeNodeProvider provider)
    {
        Node = node;
        _provider = provider;
    }
}
