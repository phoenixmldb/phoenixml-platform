using Phoenixml.Editor.Views.SyntaxHighlighting;
using Phoenixml.Platform.Abstractions.Documents;
using Xunit;

namespace Phoenixml.Editor.Tests.Views.SyntaxHighlighting;

public class HighlightingRegistryTests
{
    [Fact]
    public void XQueryReturnsNonNullDefinition()
    {
        var reg = new HighlightingRegistry();
        var def = reg.Resolve(DocumentKind.XQuery);
        Assert.NotNull(def);
        Assert.Equal("XQuery", def!.Name);
    }

    [Fact]
    public void UnknownReturnsNull()
    {
        var reg = new HighlightingRegistry();
        Assert.Null(reg.Resolve(DocumentKind.Unknown));
    }

    [Fact]
    public void ResultIsCached()
    {
        var reg = new HighlightingRegistry();
        var first = reg.Resolve(DocumentKind.XQuery);
        var second = reg.Resolve(DocumentKind.XQuery);
        Assert.Same(first, second);
    }
}
