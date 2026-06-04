using Phoenixml.Workbench.Lsp;
using Xunit;

namespace Phoenixml.Editor.Tests.Lsp;

public class LspDiagnosticBridgeTests
{
    [Fact]
    public void EmptyDiagnosticsProducesEmptyList()
    {
        var p = new LspPublishDiagnosticsParams("file:///x.xq", []);
        var result = LspDiagnosticBridge.Convert(p);
        Assert.Empty(result);
    }

    [Fact]
    public void ConvertsBasicDiagnostic()
    {
        var p = new LspPublishDiagnosticsParams("file:///x.xq",
            [new LspDiagnostic(
                new LspRange(new LspPosition(3, 5), new LspPosition(3, 12)),
                Severity: 1, Code: "XQST0003", Source: "xquery",
                Message: "unexpected token")]);

        var result = LspDiagnosticBridge.Convert(p);

        var d = Assert.Single(result);
        Assert.Equal("file:///x.xq", d.Uri);
        Assert.Equal(3, d.Line);
        Assert.Equal(5, d.Column);
        Assert.Equal(7, d.Length);
        Assert.Equal(1, d.Severity);
        Assert.Equal("XQST0003", d.Code);
        Assert.Equal("xquery", d.Source);
        Assert.Equal("unexpected token", d.Message);
    }

    [Fact]
    public void ConvertsMultipleDiagnostics()
    {
        var p = new LspPublishDiagnosticsParams("file:///y.xsl",
        [
            new LspDiagnostic(new LspRange(new LspPosition(0, 0), new LspPosition(0, 1)),
                Severity: 1, Code: "E1", Source: "xslt", Message: "first"),
            new LspDiagnostic(new LspRange(new LspPosition(1, 0), new LspPosition(1, 1)),
                Severity: 2, Code: "W1", Source: "xslt", Message: "second"),
        ]);

        var result = LspDiagnosticBridge.Convert(p);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Severity);
        Assert.Equal(2, result[1].Severity);
    }
}
