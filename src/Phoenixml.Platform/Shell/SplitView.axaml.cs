using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;

namespace Phoenixml.Platform.Shell;

/// <summary>
/// A two-pane split layout with a draggable <see cref="GridSplitter"/> between the panes.
/// Use <see cref="Orientation"/> to choose horizontal (side-by-side) or vertical (top/bottom).
/// </summary>
/// <remarks>
/// v0.3 ships a minimal working split. Real dockable panes (detach, regroup, save layout)
/// are Plan 6 — not yet earned by v0.3 consumers.
/// </remarks>
public sealed partial class SplitView : UserControl
{
    /// <summary>
    /// Identifies the <see cref="Orientation"/> Avalonia property.
    /// </summary>
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<SplitView, Orientation>(
            nameof(Orientation),
            defaultValue: Orientation.Horizontal);

    /// <summary>
    /// Identifies the <see cref="Pane1Size"/> Avalonia property.
    /// </summary>
    public static readonly StyledProperty<double> Pane1SizeProperty =
        AvaloniaProperty.Register<SplitView, double>(
            nameof(Pane1Size),
            defaultValue: 300.0);

    /// <summary>Initializes a new instance of <see cref="SplitView"/>.</summary>
    public SplitView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Gets or sets the split orientation.
    /// <see cref="Orientation.Horizontal"/> = panes side-by-side;
    /// <see cref="Orientation.Vertical"/> = panes top/bottom.
    /// </summary>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the size (width when horizontal, height when vertical) of pane 1 in DIPs.
    /// </summary>
    public double Pane1Size
    {
        get => GetValue(Pane1SizeProperty);
        set => SetValue(Pane1SizeProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == OrientationProperty)
            ApplyLayout();
        else if (change.Property == Pane1SizeProperty && change.NewValue is double size)
            ApplyPane1Size(size);
    }

    /// <summary>Gets or sets the content of pane 1 (left/top).</summary>
    public object? Pane1Content
    {
        get => this.FindControl<ContentPresenter>("Pane1Presenter")?.Content;
        set
        {
            var p = this.FindControl<ContentPresenter>("Pane1Presenter");
            if (p is not null) p.Content = value;
        }
    }

    /// <summary>Gets or sets the content of pane 2 (right/bottom).</summary>
    public object? Pane2Content
    {
        get => this.FindControl<ContentPresenter>("Pane2Presenter")?.Content;
        set
        {
            var p = this.FindControl<ContentPresenter>("Pane2Presenter");
            if (p is not null) p.Content = value;
        }
    }

    private void ApplyLayout()
    {
        var grid = this.FindControl<Grid>("RootGrid");
        var splitter = this.FindControl<GridSplitter>("Splitter");
        var p1 = this.FindControl<ContentPresenter>("Pane1Presenter");
        var p2 = this.FindControl<ContentPresenter>("Pane2Presenter");
        if (grid is null || splitter is null || p1 is null || p2 is null) return;

        if (Orientation == Orientation.Horizontal)
        {
            // Side-by-side: three columns
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition(Pane1Size, GridUnitType.Pixel));
            grid.ColumnDefinitions.Add(new ColumnDefinition(4, GridUnitType.Pixel));
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            grid.RowDefinitions.Clear();
            grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));

            Grid.SetColumn(p1, 0); Grid.SetRow(p1, 0); Grid.SetColumnSpan(p1, 1); Grid.SetRowSpan(p1, 1);
            Grid.SetColumn(splitter, 1); Grid.SetRow(splitter, 0);
            Grid.SetColumn(p2, 2); Grid.SetRow(p2, 0); Grid.SetColumnSpan(p2, 1); Grid.SetRowSpan(p2, 1);

            splitter.ResizeDirection = GridResizeDirection.Columns;
        }
        else
        {
            // Top/bottom: three rows
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            grid.RowDefinitions.Clear();
            grid.RowDefinitions.Add(new RowDefinition(Pane1Size, GridUnitType.Pixel));
            grid.RowDefinitions.Add(new RowDefinition(4, GridUnitType.Pixel));
            grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));

            Grid.SetRow(p1, 0); Grid.SetColumn(p1, 0); Grid.SetRowSpan(p1, 1); Grid.SetColumnSpan(p1, 1);
            Grid.SetRow(splitter, 1); Grid.SetColumn(splitter, 0);
            Grid.SetRow(p2, 2); Grid.SetColumn(p2, 0); Grid.SetRowSpan(p2, 1); Grid.SetColumnSpan(p2, 1);

            splitter.ResizeDirection = GridResizeDirection.Rows;
        }
    }

    private void ApplyPane1Size(double size)
    {
        var grid = this.FindControl<Grid>("RootGrid");
        if (grid is null) return;

        if (Orientation == Orientation.Horizontal && grid.ColumnDefinitions.Count == 3)
            grid.ColumnDefinitions[0] = new ColumnDefinition(size, GridUnitType.Pixel);
        else if (Orientation == Orientation.Vertical && grid.RowDefinitions.Count == 3)
            grid.RowDefinitions[0] = new RowDefinition(size, GridUnitType.Pixel);
    }
}
