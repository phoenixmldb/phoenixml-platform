using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Phoenixml.Platform.Editing;

/// <summary>
/// Horizontal breadcrumb showing the ancestor path of the current selection.
/// Click any segment to navigate.
/// </summary>
public sealed partial class BreadcrumbView : UserControl
{
    /// <summary>Initializes a new instance of <see cref="BreadcrumbView"/>.</summary>
    public BreadcrumbView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Raised when the user clicks a breadcrumb segment.</summary>
#pragma warning disable CA1003 // Use generic event handler instances — intentional; Action<T> pattern
    public event Action<BreadcrumbSegment>? SegmentClicked;
#pragma warning restore CA1003

    /// <summary>Replaces the segments. Last segment is rendered without a trailing chevron.</summary>
    public void SetPath(IReadOnlyList<BreadcrumbSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var withFlags = segments
            .Select((s, i) => new BreadcrumbSegmentRow(s, i < segments.Count - 1))
            .ToList();

        var host = this.FindControl<ItemsControl>("SegmentsHost");
        if (host is not null) host.ItemsSource = withFlags;
    }

    private void Segment_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is BreadcrumbSegment segment)
            SegmentClicked?.Invoke(segment);
    }
}

/// <summary>One segment in a <see cref="BreadcrumbView"/> path.</summary>
/// <param name="Id">Stable identifier for this segment.</param>
/// <param name="Label">Display label shown in the breadcrumb.</param>
/// <param name="Payload">Optional host-defined data passed back via <c>SegmentClicked</c>.</param>
public sealed record BreadcrumbSegment(string Id, string Label, object? Payload = null);

/// <summary>Internal row view-model used by the breadcrumb DataTemplate.</summary>
internal sealed class BreadcrumbSegmentRow(BreadcrumbSegment segment, bool hasNext)
{
    /// <summary>The underlying segment.</summary>
    public BreadcrumbSegment Segment { get; } = segment;

    /// <summary>Display label (forwarded from <see cref="Segment"/>).</summary>
    public string Label => Segment.Label;

    /// <summary>True when a chevron separator should be rendered after this segment.</summary>
    public bool HasNext { get; } = hasNext;
}
