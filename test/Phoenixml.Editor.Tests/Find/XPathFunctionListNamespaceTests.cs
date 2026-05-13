using Phoenixml.Editor.Find;
using Xunit;

namespace Phoenixml.Editor.Tests.Find;

public class XPathFunctionListNamespaceTests
{
    [Fact]
    public void MapPrefixReturnsOnlyMapFunctions()
    {
        var matches = XPathFunctionList.Match("map:");
        Assert.Contains("map:keys", matches);
        Assert.DoesNotContain("count", matches);
    }

    [Fact]
    public void ArrayPrefixReturnsOnlyArrayFunctions()
    {
        var matches = XPathFunctionList.Match("array:");
        Assert.Contains("array:size", matches);
        Assert.DoesNotContain("count", matches);
    }

    [Fact]
    public void XsPrefixReturnsOnlyXsTypes()
    {
        var matches = XPathFunctionList.Match("xs:");
        Assert.Contains("xs:string", matches);
        Assert.DoesNotContain("count", matches);
    }

    [Fact]
    public void NoPrefixReturnsFnFunctionsOnly()
    {
        var matches = XPathFunctionList.Match("coun");
        Assert.Contains("count", matches);
        Assert.DoesNotContain("map:keys", matches);
    }
}
