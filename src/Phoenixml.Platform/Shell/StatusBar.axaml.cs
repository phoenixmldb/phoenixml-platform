using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Phoenixml.Platform.Shell;

/// <summary>
/// Composable status bar. Register segments via <see cref="SetSegments"/> at startup.
/// Apps typically call this from <see cref="Hosting.PhoenixmlAppBuilder"/> wiring.
/// </summary>
public sealed partial class StatusBar : UserControl
{
    /// <summary>Initializes a new instance of <see cref="StatusBar"/>.</summary>
    public StatusBar()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Replaces the displayed segments. Segments are sorted by <see cref="StatusBarSegment.Order"/>
    /// and rendered left-to-right with separators.
    /// </summary>
    public void SetSegments(IEnumerable<StatusBarSegment> segments, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(services);

        var ordered = segments
            .OrderBy(s => s.Order)
            .Select(s => s.DynamicTextFactory?.Invoke(services) ?? s.Label)
            .ToList();

        var host = this.FindControl<ItemsControl>("SegmentsHost");
        if (host is not null)
            host.ItemsSource = ordered;
    }
}
