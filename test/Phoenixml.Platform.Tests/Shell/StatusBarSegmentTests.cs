using Phoenixml.Platform.Shell;
using Xunit;

namespace Phoenixml.Platform.Tests.Shell;

public class StatusBarSegmentTests
{
    [Fact]
    public void SegmentOrdersByOrderField()
    {
        var a = new StatusBarSegment("a", "Alpha", 30);
        var b = new StatusBarSegment("b", "Beta", 10);
        var c = new StatusBarSegment("c", "Gamma", 20);

        var sorted = new[] { a, b, c }.OrderBy(s => s.Order).ToList();

        Assert.Equal("b", sorted[0].Id);
        Assert.Equal("c", sorted[1].Id);
        Assert.Equal("a", sorted[2].Id);
    }

    [Fact]
    public void SegmentDynamicTextFactoryOverridesLabel()
    {
        var segment = new StatusBarSegment(
            "dyn",
            "Static Label",
            1,
            DynamicTextFactory: _ => "Dynamic Text");

        // Simulate what StatusBar.SetSegments does when rendering each segment.
        var rendered = segment.DynamicTextFactory!.Invoke(null!);
        Assert.Equal("Dynamic Text", rendered);
    }

    [Fact]
    public void SegmentLabelUsedWhenNoFactory()
    {
        var segment = new StatusBarSegment("static", "My Label", 1);
        Assert.Null(segment.DynamicTextFactory);
        Assert.Equal("My Label", segment.Label);
    }

    [Fact]
    public void SegmentRecordEquality()
    {
        var a = new StatusBarSegment("x", "Label", 5);
        var b = new StatusBarSegment("x", "Label", 5);
        Assert.Equal(a, b);
    }
}
