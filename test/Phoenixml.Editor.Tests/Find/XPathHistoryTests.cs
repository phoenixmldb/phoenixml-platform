using Phoenixml.Workbench.Find;
using Xunit;

namespace Phoenixml.Editor.Tests.Find;

public class XPathHistoryTests
{
    [Fact]
    public void RecordAppendsToFront()
    {
        var h = new XPathHistory(capacity: 5);
        h.Record("//a");
        h.Record("//b");
        Assert.Equal(["//b", "//a"], h.Entries);
    }

    [Fact]
    public void DuplicatesAreDeduplicatedToFront()
    {
        var h = new XPathHistory(capacity: 5);
        h.Record("//a");
        h.Record("//b");
        h.Record("//a");
        Assert.Equal(["//a", "//b"], h.Entries);
    }

    [Fact]
    public void CapacityCapsTheList()
    {
        var h = new XPathHistory(capacity: 2);
        h.Record("//a");
        h.Record("//b");
        h.Record("//c");
        Assert.Equal(["//c", "//b"], h.Entries);
    }

    [Fact]
    public void EmptyAndWhitespaceAreIgnored()
    {
        var h = new XPathHistory(capacity: 5);
        h.Record("");
        h.Record("   ");
        Assert.Empty(h.Entries);
    }
}
