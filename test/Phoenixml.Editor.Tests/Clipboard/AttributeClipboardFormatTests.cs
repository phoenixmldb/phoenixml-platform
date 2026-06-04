using Phoenixml.Workbench.Clipboard;
using Xunit;

namespace Phoenixml.Editor.Tests.Clipboard;

public class AttributeClipboardFormatTests
{
    [Fact]
    public void SerializeProducesNameEqualsQuotedValue()
    {
        var s = AttributeClipboardFormat.Serialize("isbn", "978-0-13");
        Assert.Equal("isbn=\"978-0-13\"", s);
    }

    [Fact]
    public void SerializeEscapesQuotesAndAmpersands()
    {
        var s = AttributeClipboardFormat.Serialize("title", "He said \"hi\" & wave");
        Assert.Equal("title=\"He said &quot;hi&quot; &amp; wave\"", s);
    }

    [Fact]
    public void ParseRoundTrips()
    {
        var s = AttributeClipboardFormat.Serialize("k", "v\"x");
        var parsed = AttributeClipboardFormat.TryParse(s, out var name, out var value);
        Assert.True(parsed);
        Assert.Equal("k", name);
        Assert.Equal("v\"x", value);
    }

    [Fact]
    public void ParseRejectsMalformed()
    {
        Assert.False(AttributeClipboardFormat.TryParse("not an attribute", out _, out _));
        Assert.False(AttributeClipboardFormat.TryParse("=\"v\"", out _, out _));
        Assert.False(AttributeClipboardFormat.TryParse("k=v", out _, out _));
    }
}
