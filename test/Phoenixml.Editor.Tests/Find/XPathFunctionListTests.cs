using Phoenixml.Workbench.Find;
using Xunit;

namespace Phoenixml.Editor.Tests.Find;

public class XPathFunctionListTests
{
    [Fact]
    public void ReturnsAllOnEmptyPrefix()
    {
        var matches = XPathFunctionList.Match("");
        Assert.NotEmpty(matches);
    }

    [Fact]
    public void FiltersByPrefix()
    {
        var matches = XPathFunctionList.Match("coun");
        Assert.Contains("count", matches);
        Assert.DoesNotContain("position", matches);
    }

    [Fact]
    public void MatchIsCaseInsensitive()
    {
        var matches = XPathFunctionList.Match("STR");
        Assert.Contains("string", matches);
        Assert.Contains("string-length", matches);
    }
}
