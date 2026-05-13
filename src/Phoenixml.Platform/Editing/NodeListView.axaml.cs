using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    // Row VM under the right-click pointer when the context menu is about to open.
    // Avalonia ContextMenus live in a detached popup tree; DataContext + Tag bindings
    // don't resolve at Opening time. We stash the row VM via PointerPressed hit-test
    // and read it back in RowContextMenu_Opening. Consumed each Opening.
    private NodeRowViewModel? _pendingRightClickRow;

    /// <summary>Custom data-object format used to carry a row's node id between drag-source and drop-target.</summary>
    private const string NodeIdDataFormat = "application/x-phoenixml-nodeid";

    // Drag-source state.
    private NodeRowViewModel? _pendingDragRow;
    private Point _pendingDragOrigin;
    private bool _dragInProgress;

    // Row currently painted with a drop indicator. Cleared in OnRowDragLeave / after Drop.
    private NodeRowViewModel? _activeDropTargetRow;

    /// <summary>
    /// Pixel threshold the cursor must travel from <see cref="_pendingDragOrigin"/> before
    /// a click-and-hold escalates into a drag. Matches Avalonia's default text-selection drag threshold.
    /// </summary>
    private const double DragThreshold = 4.0;

    /// <summary>Initializes a new instance of <see cref="NodeListView"/>.</summary>
    public NodeListView()
    {
        InitializeComponent();
        AddHandler(InputElement.PointerPressedEvent, OnAnyPointerPressed,
            Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(InputElement.PointerMovedEvent, OnAnyPointerMoved,
            Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(InputElement.PointerReleasedEvent, OnAnyPointerReleased,
            Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, OnRowDragOver, handledEventsToo: true);
        AddHandler(DragDrop.DragLeaveEvent, OnRowDragLeave, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnRowDrop, handledEventsToo: true);
    }

    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            if (e.Source is not Avalonia.Visual src) return;
            var lbi = src.FindAncestorOfType<ListBoxItem>(includeSelf: true);
            if (lbi?.DataContext is NodeRowViewModel rv) _pendingRightClickRow = rv;
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            // Track candidate-drag origin. We only start a real drag once the pointer moves
            // beyond DragThreshold so single clicks (selection, chevron, value-cell tap) still work.
            if (e.Source is not Avalonia.Visual src) return;
            var lbi = src.FindAncestorOfType<ListBoxItem>(includeSelf: true);
            if (lbi?.DataContext is not NodeRowViewModel rv) return;

            // Skip drag if a row is in edit mode — the user is typing, not dragging.
            if (rv.IsEditing || rv.IsValueEditing) return;

            // Skip drag if click started in the chevron button — leave that gesture alone.
            if (src.FindAncestorOfType<Button>(includeSelf: true) is not null) return;

            _pendingDragRow = rv;
            _pendingDragOrigin = e.GetPosition(this);
        }
    }

    private void OnAnyPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingDragRow is null || _dragInProgress) return;
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed)
        {
            _pendingDragRow = null;
            return;
        }

        var pos = e.GetPosition(this);
        var dx = pos.X - _pendingDragOrigin.X;
        var dy = pos.Y - _pendingDragOrigin.Y;
        if (dx * dx + dy * dy < DragThreshold * DragThreshold) return;

        // Threshold crossed — start a real drag. Capture row for clean-up after the async call.
        var dragRow = _pendingDragRow;
        _pendingDragRow = null;
        _dragInProgress = true;
        dragRow.RowOpacity = 0.5;

        var data = new DataObject();
        data.Set(NodeIdDataFormat, dragRow.Node.Id);

        // Fire and forget — Avalonia's DoDragDrop is async but the result (drop effect) is
        // delivered through the DropEvent on the target. We only need to clean up source-side state.
        _ = StartDragAsync(dragRow, data, e);
    }

    private async System.Threading.Tasks.Task StartDragAsync(NodeRowViewModel dragRow, DataObject data, PointerEventArgs e)
    {
        var ghostPopup = this.FindControl<Avalonia.Controls.Primitives.Popup>("DragGhostPopup");
        var ghostText = this.FindControl<TextBlock>("DragGhostText");
        if (ghostPopup is not null && ghostText is not null)
        {
            ghostText.Text = $"Move <{dragRow.Label}>";
            ghostPopup.PlacementTarget = this;
            ghostPopup.IsOpen = true;
        }

        try
        {
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move).ConfigureAwait(true);
        }
        finally
        {
            dragRow.RowOpacity = 1.0;
            ClearDropIndicator();
            _dragInProgress = false;
            if (ghostPopup is not null) ghostPopup.IsOpen = false;
        }
    }

    private void OnAnyPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pendingDragRow = null;
    }

    private void OnRowDragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(NodeIdDataFormat))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        // Auto-scroll the ListBox when the cursor is within EdgeScrollZone of the top
        // or bottom of the visible viewport. One row per DragOver tick.
        AutoScrollOnDragOver(e);

        if (e.Source is not Avalonia.Visual src)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var lbi = src.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (lbi?.DataContext is not NodeRowViewModel targetRow)
        {
            ClearDropIndicator();
            e.DragEffects = DragDropEffects.None;
            return;
        }

        // Reject drop on self.
        var srcId = e.Data.Get(NodeIdDataFormat) as string;
        if (srcId is not null && srcId == targetRow.Node.Id)
        {
            ClearDropIndicator();
            e.DragEffects = DragDropEffects.None;
            return;
        }

        // Compute drop position from cursor Y within the target row.
        var pos = ComputeDropPosition(e.GetPosition(lbi), lbi.Bounds.Height);
        PaintDropIndicator(targetRow, pos);
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnRowDragLeave(object? sender, DragEventArgs e) => ClearDropIndicator();

    /// <summary>
    /// Auto-scrolls the row host when the cursor is near the top or bottom edge of the
    /// viewport during a drag. Step size is one row-height per DragOver tick so the
    /// scroll feels responsive but doesn't run away.
    /// </summary>
    private void AutoScrollOnDragOver(DragEventArgs e)
    {
        var host = this.FindControl<ListBox>("RowsHost");
        if (host is null) return;
        var scroll = host.Scroll;
        if (scroll is null) return;

        const double EdgeScrollZone = 24;
        const double StepDip = 18;
        var pos = e.GetPosition(host);
        var height = host.Bounds.Height;
        if (pos.Y < EdgeScrollZone)
            scroll.Offset = scroll.Offset.WithY(Math.Max(0, scroll.Offset.Y - StepDip));
        else if (pos.Y > height - EdgeScrollZone)
            scroll.Offset = scroll.Offset.WithY(scroll.Offset.Y + StepDip);
    }

    private void OnRowDrop(object? sender, DragEventArgs e)
    {
        try
        {
            if (!e.Data.Contains(NodeIdDataFormat)) return;
            if (e.Source is not Avalonia.Visual src) return;
            var lbi = src.FindAncestorOfType<ListBoxItem>(includeSelf: true);
            if (lbi?.DataContext is not NodeRowViewModel targetRow) return;

            if (e.Data.Get(NodeIdDataFormat) is not string srcId) return;
            if (srcId == targetRow.Node.Id) return;

            // Look up the source node from the flat-row list (must still be visible — which is
            // fine because the user just dragged from it).
            NodeRowViewModel? sourceRow = null;
            foreach (var r in _flatRows)
            {
                if (r.Node.Id == srcId) { sourceRow = r; break; }
            }
            if (sourceRow is null) return;

            var pos = ComputeDropPosition(e.GetPosition(lbi), lbi.Bounds.Height);
            e.Handled = true;
            NodeReorderRequested?.Invoke(sourceRow.Node, targetRow.Node, pos);
        }
        finally
        {
            ClearDropIndicator();
        }
    }

    private static DropPosition ComputeDropPosition(Point cursor, double height)
    {
        if (height <= 0) return DropPosition.AsLastChild;
        var third = height / 3.0;
        if (cursor.Y < third) return DropPosition.BeforeTarget;
        if (cursor.Y > height - third) return DropPosition.AfterTarget;
        return DropPosition.AsLastChild;
    }

    private void PaintDropIndicator(NodeRowViewModel row, DropPosition pos)
    {
        if (_activeDropTargetRow is not null && !ReferenceEquals(_activeDropTargetRow, row))
            _activeDropTargetRow.DropIndicatorThickness = new Thickness(0);

        _activeDropTargetRow = row;
        row.DropIndicatorThickness = pos switch
        {
            DropPosition.BeforeTarget => new Thickness(0, 2, 0, 0),
            DropPosition.AfterTarget  => new Thickness(0, 0, 0, 2),
            _                         => new Thickness(2),
        };
    }

    private void ClearDropIndicator()
    {
        if (_activeDropTargetRow is null) return;
        _activeDropTargetRow.DropIndicatorThickness = new Thickness(0);
        _activeDropTargetRow = null;
    }

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

    /// <summary>
    /// Raised when the user picks "Insert Child Element" from a row's context menu.
    /// The argument is the parent node into which a new element should be inserted.
    /// The host is responsible for routing to a vocabulary command (e.g. InsertElementCommand)
    /// and silently ignoring the request when the node is not a valid parent (attribute, text, etc.).
    /// </summary>
    public event Action<ITreeNode>? NodeInsertChildElementRequested;

    /// <summary>
    /// Raised when the user picks "Insert Attribute" from a row's context menu.
    /// </summary>
    public event Action<ITreeNode>? NodeInsertAttributeRequested;

    /// <summary>
    /// Raised when the user picks "Delete" from a row's context menu, or presses Delete on a
    /// selected row. The host should route to a vocabulary command (e.g. DeleteNodeCommand).
    /// </summary>
    public event Action<ITreeNode>? NodeDeleteRequested;

    /// <summary>
    /// Raised when the user picks "Copy XPath" from a row's context menu. The host is expected
    /// to build the canonical XPath for the node and write it to the system clipboard.
    /// </summary>
    public event Action<ITreeNode>? NodeCopyXPathRequested;

    /// <summary>
    /// Raised just before the row context menu opens so the host can populate schema-aware
    /// insert options. The host sets <see cref="InsertOptions.ChildElementNames"/> and/or
    /// <see cref="InsertOptions.AttributeNames"/> from the active schema. When either list is
    /// non-empty, <see cref="NodeListView"/> builds a dynamic submenu of name choices; picking
    /// a name fires <see cref="NodeInsertChildElementWithNameRequested"/> or
    /// <see cref="NodeInsertAttributeWithNameRequested"/>. When the list is empty, the existing
    /// single-click item fires <see cref="NodeInsertChildElementRequested"/> /
    /// <see cref="NodeInsertAttributeRequested"/> as before.
    /// </summary>
    public event Action<ITreeNode, InsertOptions>? BuildingInsertOptions;

    /// <summary>
    /// Raised when the user picks a named child element from the schema-aware "Insert Child
    /// Element" submenu. The first argument is the parent node; the second is the chosen element
    /// local name from the schema. The host routes to an insert command using that name.
    /// </summary>
    public event Action<ITreeNode, string>? NodeInsertChildElementWithNameRequested;

    /// <summary>
    /// Raised when the user picks a named attribute from the schema-aware "Insert Attribute"
    /// submenu. The first argument is the parent node; the second is the chosen attribute local
    /// name from the schema. The host routes to an insert command using that name.
    /// </summary>
    public event Action<ITreeNode, string>? NodeInsertAttributeWithNameRequested;

    /// <summary>
    /// Raised when the user drops a dragged row onto another row. The first argument is the
    /// dragged source node; the second is the drop-target node; the third indicates where
    /// the source should be placed relative to the target (last child, before, or after).
    /// The host is responsible for routing to a vocabulary command (e.g. MoveSubtreeCommand
    /// for XML) and silently ignoring drops that would create cycles or violate invariants.
    /// </summary>
    public event Action<ITreeNode, ITreeNode, DropPosition>? NodeReorderRequested;
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

    /// <summary>
    /// Expands all ancestors of the node with the given <paramref name="id"/> so it becomes
    /// visible, then selects it. Walks the provider tree depth-first looking for the target;
    /// along the path back from the match, marks each ancestor expanded. Rebuilds the flat-row
    /// list and selects. No-op if the id is not found in the tree.
    /// </summary>
    public void EnsureVisibleAndSelect(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_provider is null) return;
        foreach (var root in _provider.Roots)
        {
            if (TryExpandPathTo(root, id))
            {
                Rebuild();
                SelectById(id);
                return;
            }
        }
    }

    private bool TryExpandPathTo(ITreeNode current, string targetId)
    {
        if (current.Id == targetId) return true;
        if (_provider is null) return false;
        foreach (var child in _provider.GetChildren(current))
        {
            if (TryExpandPathTo(child, targetId))
            {
                _expansionState[current.Id] = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Marks the row with the given <paramref name="id"/> as expanded. Hosts call this before
    /// inserting a child so the new row becomes visible after the resulting Rebuild. No-op if
    /// the id is unknown.
    /// </summary>
    public void ExpandById(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        _expansionState[id] = true;
    }

    /// <summary>
    /// Selects the row with the given <paramref name="id"/> and immediately enters name-edit
    /// mode (the same state as pressing F2). Focuses the inline TextBox and selects all its
    /// text so the user can type to replace the placeholder name in one gesture.
    /// </summary>
    public void BeginRenameById(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var host = this.FindControl<ListBox>("RowsHost");
        if (host is null) return;

        int targetIndex = -1;
        NodeRowViewModel? targetRow = null;
        for (int i = 0; i < _flatRows.Count; i++)
        {
            if (_flatRows[i].Node.Id == id)
            {
                targetIndex = i;
                targetRow = _flatRows[i];
                break;
            }
        }
        if (targetRow is null) return;

        host.SelectedIndex = targetIndex;
        targetRow.EditingText = targetRow.EditableName;
        targetRow.IsEditing = true;

        // Container realization is async — the TextBox isn't in the visual tree yet. Defer
        // focusing until Avalonia has materialized the row, then pick the visible TextBox in
        // its visual tree (the IsEditing-bound name TextBox) and SelectAll for one-gesture rename.
        Dispatcher.UIThread.Post(() =>
        {
            var container = host.ContainerFromIndex(targetIndex);
            if (container is null) return;
            var textBox = FindFirstVisibleTextBox(container);
            if (textBox is null) return;
            textBox.Focus();
            textBox.SelectAll();
        }, DispatcherPriority.Background);
    }

    private static TextBox? FindFirstVisibleTextBox(Avalonia.Visual root)
    {
        foreach (var child in root.GetVisualDescendants())
        {
            if (child is TextBox tb && tb.IsVisible) return tb;
        }
        return null;
    }

    /// <summary>
    /// Selects the row with the given <paramref name="id"/> and immediately enters value-edit
    /// mode (the same state as clicking into the value cell). Focuses the inline value
    /// <see cref="TextBox"/> and selects all its text so the user can type to replace the
    /// placeholder content in one gesture.
    /// </summary>
    /// <remarks>
    /// Foundation for schema-aware Insert auto-focus: after inserting a simple-typed element
    /// such as <c>&lt;title&gt;</c>, the host calls this so the user can type the element's
    /// text content immediately without an extra click.
    /// </remarks>
    /// <param name="id">The <see cref="ITreeNode.Id"/> of the row to enter value-edit mode on.</param>
    public void BeginValueEditById(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var host = this.FindControl<ListBox>("RowsHost");
        if (host is null) return;

        int targetIndex = -1;
        NodeRowViewModel? targetRow = null;
        for (int i = 0; i < _flatRows.Count; i++)
        {
            if (_flatRows[i].Node.Id == id)
            {
                targetIndex = i;
                targetRow = _flatRows[i];
                break;
            }
        }
        if (targetRow is null) return;

        host.SelectedIndex = targetIndex;
        targetRow.EditingValue = targetRow.Value;
        targetRow.IsValueEditing = true;

        // Container realization is async — the TextBox isn't in the visual tree yet. Defer
        // focusing until Avalonia has materialized the row, then locate the value-column
        // TextBox (identified by its row VM's IsValueEditing flag) and SelectAll.
        Dispatcher.UIThread.Post(() =>
        {
            var container = host.ContainerFromIndex(targetIndex);
            if (container is null) return;
            var textBox = FindValueColumnTextBox(container);
            if (textBox is null) return;
            textBox.Focus();
            textBox.SelectAll();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Finds the value-column <see cref="TextBox"/> within a realized row container.
    /// Discriminates from the name-column TextBox by requiring both
    /// <c>IsVisible</c> and <c>DataContext.IsValueEditing == true</c> on the
    /// row view-model. In normal operation only one of the two TextBoxes is visible at a time,
    /// but the <see cref="NodeRowViewModel.IsValueEditing"/> guard makes the lookup explicit.
    /// </summary>
    private static TextBox? FindValueColumnTextBox(Avalonia.Visual root)
    {
        foreach (var child in root.GetVisualDescendants())
        {
            if (child is TextBox tb && tb.IsVisible
                && tb.DataContext is NodeRowViewModel row && row.IsValueEditing)
                return tb;
        }
        return null;
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
        if (sender is not ListBox lb || lb.SelectedItem is not NodeRowViewModel row) return;

        if (e.Key == Key.F2)
        {
            row.EditingText = row.EditableName;
            row.IsEditing = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            NodeDeleteRequested?.Invoke(row.Node);
            e.Handled = true;
        }
    }

    private void InsertChildElement_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is NodeRowViewModel row)
            NodeInsertChildElementRequested?.Invoke(row.Node);
    }

    private void InsertAttribute_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is NodeRowViewModel row)
            NodeInsertAttributeRequested?.Invoke(row.Node);
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is NodeRowViewModel row)
            NodeDeleteRequested?.Invoke(row.Node);
    }

    private void CopyXPath_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is NodeRowViewModel row)
            NodeCopyXPathRequested?.Invoke(row.Node);
    }

    /// <summary>
    /// Handles the <see cref="ContextMenu.Opening"/> event fired before a row's context menu
    /// appears. Fires <see cref="BuildingInsertOptions"/> so the host can populate schema-aware
    /// name lists. If names are provided, replaces the static "Insert Child Element" /
    /// "Insert Attribute" items with a dynamic submenu; otherwise restores direct-click mode
    /// by clearing <see cref="ItemsControl.ItemsSource"/> so the AXAML-wired
    /// <c>Click</c> handler fires normally.
    /// </summary>
    private void RowContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;

        // Use the stashed row VM from the right-click PointerPressed hit-test.
        // ContextMenu DataContext + Tag bindings don't settle until AFTER the popup opens,
        // so we can't read them here; the PointerPressed handler ran before this and
        // captured the target row's NodeRowViewModel.
        var rowVm = _pendingRightClickRow;
        _pendingRightClickRow = null;  // consume
        if (rowVm is null) return;

        var options = new InsertOptions();
        BuildingInsertOptions?.Invoke(rowVm.Node, options);

        // Index-based lookup matching AXAML order: [0]=InsertChild, [1]=InsertAttribute,
        // [2]=Separator, [3]=Delete. (Header-text lookup is brittle since Avalonia may
        // wrap headers in AccessText/etc.)
        var insertChildItem = menu.Items.Count > 0 ? menu.Items[0] as MenuItem : null;
        var insertAttrItem = menu.Items.Count > 1 ? menu.Items[1] as MenuItem : null;

        if (insertChildItem is not null)
            ConfigureInsertItem(insertChildItem, rowVm.Node, options.ChildElementNames,
                name => NodeInsertChildElementWithNameRequested?.Invoke(rowVm.Node, name));

        if (insertAttrItem is not null)
            ConfigureInsertItem(insertAttrItem, rowVm.Node, options.AttributeNames,
                name => NodeInsertAttributeWithNameRequested?.Invoke(rowVm.Node, name));
    }

    /// <summary>
    /// Configures <paramref name="item"/> as either a direct-click item (when
    /// <paramref name="names"/> is empty) or a submenu populated with one entry per name.
    /// When in submenu mode, clicking a child item fires <paramref name="withName"/>.
    /// When in direct-click mode, <c>ItemsSource</c> is cleared so the AXAML-wired
    /// <c>Click</c> handler (for the no-schema fallback) remains active.
    /// </summary>
    private static void ConfigureInsertItem(
        MenuItem item,
        ITreeNode node,
        IReadOnlyList<string> names,
        Action<string> withName)
    {
        if (names.Count == 0)
        {
            // Restore direct-click mode: clear any previous submenu so Avalonia uses the
            // Click handler wired in AXAML (InsertChildElement_Click / InsertAttribute_Click).
            item.ItemsSource = null;
        }
        else
        {
            // Build a fresh submenu list. Re-creating on every Opening avoids stale handlers
            // from previous menu opens. The list is small (schema children) so allocation cost
            // is negligible.
            var subItems = new List<MenuItem>(names.Count);
            foreach (var name in names)
            {
                var sub = new MenuItem { Header = name };
                var capturedName = name;
                sub.Click += (_, _) => withName(capturedName);
                subItems.Add(sub);
            }
            item.ItemsSource = subItems;
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
        if (string.IsNullOrEmpty(trimmed) || trimmed == row.EditableName) return;
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

    /// <summary>Editable name forwarded from <see cref="Node"/>.</summary>
    public string EditableName => Node.EditableName;

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

    /// <summary>
    /// Diagnostic overlay glyph derived from <see cref="ITreeNode.IconKey"/>.
    /// Returns <c>"✕"</c> for error, <c>"⚠"</c> for warning, and
    /// <see cref="string.Empty"/> when there is no diagnostic annotation.
    /// </summary>
    public string DiagnosticGlyph => Node.IconKey switch
    {
        "diag.error"   => "✕",
        "diag.warning" => "⚠",
        _              => string.Empty,
    };

    /// <summary>
    /// Brush for the <see cref="DiagnosticGlyph"/>. Red for errors, yellow for warnings,
    /// transparent when there is no diagnostic — so the 16-dip slot takes up no visual
    /// space when the row is clean.
    /// </summary>
    public IBrush DiagnosticGlyphBrush => Node.IconKey switch
    {
        "diag.error"   => new SolidColorBrush(Color.Parse("#C42B1C")),
        "diag.warning" => new SolidColorBrush(Color.Parse("#CA8B00")),
        _              => Brushes.Transparent,
    };

    /// <summary>Match-highlight state forwarded from <see cref="Node"/>.</summary>
    public MatchHighlight MatchHighlight => Node.MatchHighlight;

    /// <summary>Diff-highlight state forwarded from <see cref="Node"/>.</summary>
    public DiffHighlight DiffHighlight => Node.DiffHighlight;

    /// <summary>
    /// Brush for the row's background tint. Diff tint (Plan 24) takes precedence over
    /// find-match tint (Plan 15) when both are active — the compare view doesn't host
    /// the find bar anyway, so the two never normally coexist.
    /// </summary>
    public IBrush MatchHighlightBrush => DiffHighlight switch
    {
        DiffHighlight.Added => new SolidColorBrush(Color.FromArgb(0x60, 0x1F, 0xCF, 0x4A)),    // muted green
        DiffHighlight.Removed => new SolidColorBrush(Color.FromArgb(0x60, 0xCF, 0x1F, 0x1F)),  // muted red
        DiffHighlight.Changed => new SolidColorBrush(Color.FromArgb(0x60, 0xCF, 0xB0, 0x1F)),  // muted yellow
        _ => MatchHighlight switch
        {
            MatchHighlight.ActiveMatch => new SolidColorBrush(Color.FromArgb(0xC0, 0x1E, 0x90, 0xFF)),
            MatchHighlight.Match => new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xE0, 0x66)),
            _ => Brushes.Transparent,
        },
    };

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

    private double _rowOpacity = 1.0;

    /// <summary>
    /// Row opacity. Drag-source rows dim to 0.5 while a drag is in progress; restored to 1.0
    /// when the drag completes (regardless of drop result).
    /// </summary>
    public double RowOpacity
    {
        get => _rowOpacity;
        set { if (_rowOpacity == value) return; _rowOpacity = value; OnPropertyChanged(); }
    }

    private Thickness _dropIndicatorThickness;

    /// <summary>
    /// Border thickness used to paint the drag-and-drop indicator on this row.
    /// <see cref="Thickness.Top"/>=2 for a "drop before" sibling drop, <see cref="Thickness.Bottom"/>=2
    /// for "drop after", and a uniform 2-dip border for "drop as child". Cleared (zero) when
    /// the row is not the active drop target.
    /// </summary>
    public Thickness DropIndicatorThickness
    {
        get => _dropIndicatorThickness;
        set { if (_dropIndicatorThickness == value) return; _dropIndicatorThickness = value; OnPropertyChanged(); }
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
