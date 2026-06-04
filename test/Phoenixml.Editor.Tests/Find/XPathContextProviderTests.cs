using Phoenixml.Workbench.Find;
using Xunit;

namespace Phoenixml.Editor.Tests.Find;

public class XPathContextProviderTests
{
    [Fact]
    public void EmptyInputReturnsNone()
    {
        var ctx = XPathContextProvider.DetectContext("", 0);
        Assert.Equal(XPathContextKind.None, ctx.Kind);
        Assert.Equal("", ctx.Prefix);
    }

    [Fact]
    public void AfterDoubleSlashIsElementName()
    {
        var ctx = XPathContextProvider.DetectContext("//", 2);
        Assert.Equal(XPathContextKind.ElementName, ctx.Kind);
        Assert.Equal("", ctx.Prefix);
    }

    [Fact]
    public void PartialElementNameAfterSlash()
    {
        var ctx = XPathContextProvider.DetectContext("//boo", 5);
        Assert.Equal(XPathContextKind.ElementName, ctx.Kind);
        Assert.Equal("boo", ctx.Prefix);
    }

    [Fact]
    public void InsidePredicateAtSignIsAttributeName()
    {
        var ctx = XPathContextProvider.DetectContext("//book[@", 8);
        Assert.Equal(XPathContextKind.AttributeName, ctx.Kind);
        Assert.Equal("", ctx.Prefix);
    }

    [Fact]
    public void PartialAttributeName()
    {
        var ctx = XPathContextProvider.DetectContext("//book[@isb", 11);
        Assert.Equal(XPathContextKind.AttributeName, ctx.Kind);
        Assert.Equal("isb", ctx.Prefix);
    }

    [Fact]
    public void StandaloneWordIsFunctionName()
    {
        var ctx = XPathContextProvider.DetectContext("coun", 4);
        Assert.Equal(XPathContextKind.FunctionName, ctx.Kind);
        Assert.Equal("coun", ctx.Prefix);
    }
}
