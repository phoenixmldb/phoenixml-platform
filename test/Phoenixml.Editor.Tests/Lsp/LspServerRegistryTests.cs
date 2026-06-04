using System.IO;
using Phoenixml.Workbench.Lsp;
using Phoenixml.Platform.Abstractions.Documents;
using Xunit;

namespace Phoenixml.Editor.Tests.Lsp;

public class LspServerRegistryTests
{
    [Fact]
    public void ResolvesXQueryBinaryByDocumentKind()
    {
        var reg = new LspServerRegistry(rootDir: Path.Combine("opt", "phoenixml"));
        var path = reg.PathFor(DocumentKind.XQuery);
        Assert.NotNull(path);
        Assert.EndsWith(Path.Combine("tools", "phoenixmldb-xquery-lsp"), path, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvesXsltBinaryByDocumentKind()
    {
        var reg = new LspServerRegistry(rootDir: Path.Combine("opt", "phoenixml"));
        var path = reg.PathFor(DocumentKind.Xslt);
        Assert.NotNull(path);
        Assert.EndsWith(Path.Combine("tools", "phoenixmldb-xslt-lsp"), path, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnsNullForUnsupportedKind()
    {
        var reg = new LspServerRegistry(rootDir: Path.Combine("opt", "phoenixml"));
        Assert.Null(reg.PathFor(DocumentKind.Xml));
        Assert.Null(reg.PathFor(DocumentKind.Xsd));
        Assert.Null(reg.PathFor(DocumentKind.Unknown));
    }
}
