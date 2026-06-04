using Phoenixml.Workbench.Find;
using Xunit;

namespace Phoenixml.Editor.Tests.Find;

public class XPathSignaturesTests
{
    [Fact]
    public void ReturnsNullForUnknownFunction()
    {
        Assert.Null(XPathSignatures.Get("not-a-real-fn"));
    }

    [Fact]
    public void ReturnsSignatureForCommonFunction()
    {
        Assert.Equal("count($arg as item()*) as xs:integer", XPathSignatures.Get("count"));
    }

    [Fact]
    public void IsCaseSensitive()
    {
        Assert.Null(XPathSignatures.Get("Count"));
        Assert.NotNull(XPathSignatures.Get("count"));
    }
}
