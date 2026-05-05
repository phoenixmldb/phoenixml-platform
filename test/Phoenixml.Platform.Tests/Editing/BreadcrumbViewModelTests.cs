using Phoenixml.Platform.Editing;
using Xunit;

namespace Phoenixml.Platform.Tests.Editing;

public class BreadcrumbViewModelTests
{
    [Fact]
    public void SegmentRecordEqualityMatchesOnAllFields()
    {
        var a = new BreadcrumbSegment("id1", "Label One", null);
        var b = new BreadcrumbSegment("id1", "Label One", null);
        Assert.Equal(a, b);
    }

    [Fact]
    public void SegmentRecordInequalityOnDifferentId()
    {
        var a = new BreadcrumbSegment("id1", "Label");
        var b = new BreadcrumbSegment("id2", "Label");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SegmentWithExpressionPreservesPayload()
    {
        var payload = new object();
        var original = new BreadcrumbSegment("doc", "Document", payload);
        var modified = original with { Label = "Modified Label" };

        Assert.Equal("Modified Label", modified.Label);
        Assert.Same(payload, modified.Payload);
        Assert.Equal("doc", modified.Id);
    }

    [Fact]
    public void SegmentPayloadNullByDefault()
    {
        var seg = new BreadcrumbSegment("root", "Root");
        Assert.Null(seg.Payload);
    }

    [Fact]
    public void SegmentPreservesArbitraryPayloadType()
    {
        var payload = new { Line = 5, Column = 3 };
        var seg = new BreadcrumbSegment("line", "Line 5", payload);
        Assert.Same(payload, seg.Payload);
    }
}
