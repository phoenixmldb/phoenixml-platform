using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Phoenixml.Platform.Abstractions.Editing;

namespace Phoenixml.Platform.Editing;

/// <summary>
/// XmlNotepad-style dual-column node viewer. Renders a flat list of visible (expanded) tree
/// nodes with two aligned columns: the name column on the left (with depth indent and chevron)
/// and the value column on the right. All rows share the same fixed column widths so values
/// align vertically regardless of nesting depth.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="TreeEditorView"/> (which delegates expansion to Avalonia's
/// <c>TreeView</c> container), this control maintains its own flat <c>_flatRows</c> list and
/// re-builds it whenever the user toggles a chevron or the provider fires its
/// <see cref="ITreeNodeProvider.Changed"/> event. This approach makes shared column widths
/// trivial — a single <c>ListBox</c> with a two-column <c>DataTemplate</c>.
/// </para>
/// <para>
/// F2 inline rename works the same as in <see cref="TreeEditorView"/>: F2 on a selected row
/// switches it to edit mode; Enter commits via <see cref="NodeRenameRequested"/>; Escape
/// cancels; LostFocus commits too.
/// </para>
/// </remarks>
public sealed partial class NodeListView : UserControl
{
    private readonly List<NodeRowViewModel> _flatRows = new();
    private readonly Dictionary<string, bool> _expansionState = new();
    private ITreeNodeProvider? _provider;

    /// <summary>Initializes a new instance of <see cref="NodeListView"/>.</summary>
    public NodeListView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Gets the currently active node provider.</summary>
    public ITreeNodeProvider? Provider => _provider;

    /// <summary>Gets the currently selected node, or <see langword="null"/> when nothing is selected.</summary>
    public ITreeNode? SelectedNode { get; private set; }

    /// <summary>Raised when the user changes the selected row.</summary>
#pragma warning disable CA1003 // Use generic event handler instances — intentional; Action<T> pattern
#pragma warning disable CS0067 // NodeActivated not yet raised — reserved to mirror TreeEditorView contract
    public event Action<ITreeNode>? SelectionChanged;

    /// <summary>Raised when the user double-activates a row (reserved for future use).</summary>
    public event Action<ITreeNode>? NodeActivated;
#pragma warning restore CS0067

    /// <summary>
    /// Raised when the user commits an F2 inline rename (Enter or LostFocus with a changed name).
    /// The first argument is the node being renamed; the second is the new label text.
    /// </summary>
    public event Action<ITreeNode, string>? NodeRenameRequested;

    /// <summary>
    /// Raised when the user commits a value-column inline edit (Enter or LostFocus with a changed value).
    /// The first argument is the node whose value should change; the second is the new value text.
    /// The host is responsible for routing to the appropriate vocabulary command (e.g.
    /// SetAttributeValueCommand or SetTextContentCommand for XML).
    /// </summary>
    public event Action<ITreeNode, string>? NodeValueChangeRequested;
#pragma warning restore CA1003

    /// <summary>
    /// Replaces the active provider and rebuilds the list. Pass <see langword="null"/> to clear.
    /// Subscribes to <see cref="ITreeNodeProvider.Changed"/> on the new provider and
    /// default-expands the first two levels.
    /// </summary>
    public void SetProvider(ITreeNodeProvider? provider)
    {
        if (_provider is not null)
            _provider.Changed -= OnProviderChanged;

        _provider = provider;

        if (_provider is not null)
            _provider.Changed += OnProviderChanged;

        // Default-expand the first three levels on initial load. Two levels often shows only
        // container elements (catalog → category → ...) where the value column stays empty;
        // a third level reaches leaves with actual values to display.
        _expansionState.Clear();
        if (_provider is not null)
        {
            foreach (var root in _provider.Roots)
                ExpandRecursively(root, depth: 0, maxDepth: 2);
        }

        Rebuild();
    }

    private void ExpandRecursively(ITreeNode node, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        _expansionState[node.Id] = true;
        if (_provider is null) return;
        foreach (var child in _provider.GetChildren(node))
            ExpandRecursively(child, depth + 1, maxDepth);
    }

    /// <summary>
    /// Selects the row whose node has the given <paramref name="id"/>.
    /// No-op if the id is not found among the currently visible rows.
    /// </summary>
    public void SelectById(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var host = this.FindControl<ListBox>("RowsHost");
        if (host is null) return;
        for (int i = 0; i < _flatRows.Count; i++)
        {
            if (_flatRows[i].Node.Id == id)
            {
                host.SelectedIndex = i;
                return;
            }
        }
    }

    private void OnProviderChanged()
    {
        // Document.Changed can fire on a worker thread (e.g. SaveAsAsync's MarkClean
        // resumes off the UI thread after file I/O). Rebuild touches Avalonia controls
        // (FindControl, ListBox.ItemsSource) which require the UI thread.
        if (Dispatcher.UIThread.CheckAccess())
            Rebuild();
        else
            Dispatcher.UIThread.Post(Rebuild);
    }

    private void Rebuild()
    {
        var host = this.FindControl<ListBox>("RowsHost");
        if (host is null) return;

        // Preserve current selection so refreshes (e.g. after rename) don't lose focus.
        var selectedId = SelectedNode?.Id;

        _flatRows.Clear();
        if (_provider is not null)
        {
            foreach (var root in _provider.Roots)
                AppendRow(root, depth: 0);
        }

        // Snapshot to array — ListBox needs a stable collection to diff against.
        host.ItemsSource = _flatRows.ToArray();

        if (selectedId is not null)
            SelectById(selectedId);
    }

    private void AppendRow(ITreeNode node, int depth)
    {
        var isExpanded = _expansionState.TryGetValue(node.Id, out var e) && e;
        var row = new NodeRowViewModel(node, depth)
        {
            HasChildren = node.HasChildren,
            IsExpanded = isExpanded,
        };
        _flatRows.Add(row);

        if (isExpanded && _provider is not null && node.HasChildren)
        {
            foreach (var child in _provider.GetChildren(node))
                AppendRow(child, depth + 1);
        }
    }

    private void Chevron_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is NodeRowViewModel row)
        {
            _expansionState[row.Node.Id] = !row.IsExpanded;
            Rebuild();
        }
    }

    private void RowsHost_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is NodeRowViewModel row)
        {
            SelectedNode = row.Node;
            SelectionChanged?.Invoke(row.Node);
        }
    }

    private void RowsHost_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && sender is ListBox lb && lb.SelectedItem is NodeRowViewModel row)
        {
            row.EditingText = row.Label;
            row.IsEditing = true;
            e.Handled = true;
        }
    }

    private void EditingBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not NodeRowViewModel row) return;
        if (e.Key == Key.Enter)
        {
            CommitEdit(row, tb.Text ?? string.Empty);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            row.IsEditing = false;
            e.Handled = true;
        }
    }

    private void EditingBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is NodeRowViewModel row && row.IsEditing)
            CommitEdit(row, tb.Text ?? string.Empty);
    }

    private void CommitEdit(NodeRowViewModel row, string newLabel)
    {
        row.IsEditing = false;
        var trimmed = newLabel.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed == row.Label) return;
        NodeRenameRequested?.Invoke(row.Node, trimmed);
    }

    private void ValueDisplay_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is TextBlock tb && tb.Tag is NodeRowViewModel row)
        {
            // Initialize the edit field with the current value so the user edits in place,
            // not a blank field. Mirror the F2 name-edit pattern.
            row.EditingValue = row.Value;
            row.IsValueEditing = true;
            e.Handled = true;
        }
    }

    private void ValueBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not NodeRowViewModel row) return;
        if (e.Key == Key.Enter)
        {
            CommitValueEdit(row, tb.Text ?? string.Empty);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            row.IsValueEditing = false;
            e.Handled = true;
        }
    }

    private void ValueBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is NodeRowViewModel row && row.IsValueEditing)
            CommitValueEdit(row, tb.Text ?? string.Empty);
    }

    private void CommitValueEdit(NodeRowViewModel row, string newValue)
    {
        row.IsValueEditing = false;
        // No-op when the value is unchanged — avoids stamping a redundant undo entry just because
        // the user clicked into the cell and clicked away. (Empty-string values ARE meaningful
        // for attributes, so we compare against the current Value rather than checking for empty.)
        if (newValue == row.Value) return;
        NodeValueChangeRequested?.Invoke(row.Node, newValue);
    }
}

/// <summary>
/// View-model for a single visible row in <see cref="NodeListView"/>. Combines the underlying
/// <see cref="ITreeNode"/> with layout-level state (depth, expansion, inline-edit).
/// </summary>
public sealed class NodeRowViewModel : INotifyPropertyChanged
{
    /// <summary>The underlying domain node.</summary>
    public ITreeNode Node { get; }

    /// <summary>Zero-based nesting depth. Controls the indent spacer width.</summary>
    public int Depth { get; }

    /// <summary>Display label forwarded from <see cref="Node"/>.</summary>
    public string Label => Node.Label;

    /// <summary>Display value forwarded from <see cref="Node"/>.</summary>
    public string Value => Node.Value;

    /// <summary>Width of the indent spacer in device-independent pixels (depth × 16).</summary>
    public double IndentWidth => Depth * 16;

    private bool _hasChildren;

    /// <summary>Whether this node has children (controls chevron visibility).</summary>
    public bool HasChildren
    {
        get => _hasChildren;
        set { _hasChildren = value; OnPropertyChanged(); }
    }

    private bool _isExpanded;

    /// <summary>Whether this row is currently expanded (children visible).</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChevronGlyph));
        }
    }

    /// <summary>Chevron glyph: ▼ when expanded, ▶ when collapsed.</summary>
    public string ChevronGlyph => _isExpanded ? "▼" : "▶";

    private bool _isEditing;

    /// <summary>
    /// Whether this row is in inline-edit mode (TextBox visible, TextBlock hidden).
    /// Set to <see langword="true"/> on F2; cleared on Enter, Escape, or LostFocus.
    /// </summary>
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value) return;
            _isEditing = value;
            OnPropertyChanged();
        }
    }

    private string _editingText = string.Empty;

    /// <summary>
    /// The text shown in the inline edit TextBox. Initialized to <see cref="Label"/>
    /// when F2 is pressed so the user edits the current name, not a blank field.
    /// </summary>
    public string EditingText
    {
        get => _editingText;
        set
        {
            if (_editingText == value) return;
            _editingText = value;
            OnPropertyChanged();
        }
    }

    private bool _isValueEditing;

    /// <summary>
    /// Whether the value column is in inline-edit mode (TextBox visible, TextBlock hidden).
    /// Set to true when the user single-clicks the value cell; cleared on Enter, Escape, or LostFocus.
    /// Independent of <see cref="IsEditing"/> (which controls the name column).
    /// </summary>
    public bool IsValueEditing
    {
        get => _isValueEditing;
        set { if (_isValueEditing == value) return; _isValueEditing = value; OnPropertyChanged(); }
    }

    private string _editingValue = string.Empty;

    /// <summary>
    /// The text shown in the value-column inline edit TextBox. Initialized to <see cref="Value"/>
    /// when the user clicks into the value cell so they edit the current value, not a blank field.
    /// </summary>
    public string EditingValue
    {
        get => _editingValue;
        set { if (_editingValue == value) return; _editingValue = value; OnPropertyChanged(); }
    }

    /// <summary>Initializes a new <see cref="NodeRowViewModel"/> for the given node and depth.</summary>
    public NodeRowViewModel(ITreeNode node, int depth)
    {
        Node = node;
        Depth = depth;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
