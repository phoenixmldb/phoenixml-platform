using Avalonia.Layout;
using Phoenixml.Platform.Shell;
using Xunit;

namespace Phoenixml.Platform.Tests.Shell;

/// <summary>
/// Tests for <see cref="SplitView"/> property contracts.
/// Full construction requires an Avalonia platform (GridSplitter uses a cursor factory);
/// these tests verify the AvaloniaProperty defaults and metadata without constructing
/// the control. UI layout tests are deferred to manual validation with the chrome spike.
/// </summary>
public class SplitViewTests
{
    [Fact]
    public void OrientationPropertyDefaultIsHorizontal()
    {
        var defaultValue = SplitView.OrientationProperty.GetDefaultValue(typeof(SplitView));
        Assert.Equal(Orientation.Horizontal, defaultValue);
    }

    [Fact]
    public void Pane1SizePropertyDefaultIs300()
    {
        var defaultValue = SplitView.Pane1SizeProperty.GetDefaultValue(typeof(SplitView));
        Assert.Equal(300.0, defaultValue);
    }

    [Fact]
    public void OrientationPropertyIsRegisteredOnSplitView()
    {
        Assert.Equal(nameof(SplitView.Orientation), SplitView.OrientationProperty.Name);
    }

    [Fact]
    public void Pane1SizePropertyIsRegisteredOnSplitView()
    {
        Assert.Equal(nameof(SplitView.Pane1Size), SplitView.Pane1SizeProperty.Name);
    }
}
