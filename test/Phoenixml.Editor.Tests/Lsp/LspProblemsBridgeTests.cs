using Phoenixml.Workbench.Lsp;
using Phoenixml.Platform.Abstractions.Editing;
using Xunit;

namespace Phoenixml.Editor.Tests.Lsp;

public class LspProblemsBridgeTests
{
    [Fact]
    public void EmptyDiagnosticsProducesEmptyList()
    {
        var p = new LspPublishDiagnosticsParams("file:///x.xq", []);
        var result = LspProblemsBridge.Convert(p);
        Assert.Empty(result);
    }

    [Fact]
    public void SeverityMaps()
    {
        var p = new LspPublishDiagnosticsParams("file:///x.xq",
        [
            new LspDiagnostic(new LspRange(new LspPosition(0, 0), new LspPosition(0, 1)), 1, "E", "x", "err"),
            new LspDiagnostic(new LspRange(new LspPosition(0, 0), new LspPosition(0, 1)), 2, "W", "x", "warn"),
            new LspDiagnostic(new LspRange(new LspPosition(0, 0), new LspPosition(0, 1)), 3, "I", "x", "info"),
        ]);
        var result = LspProblemsBridge.Convert(p);
        Assert.Equal(DiagnosticSeverity.Error, result[0].Severity);
        Assert.Equal(DiagnosticSeverity.Warning, result[1].Severity);
        Assert.Equal(DiagnosticSeverity.Info, result[2].Severity);
    }

    [Fact]
    public void LocationCarriesUriAndOneBasedPosition()
    {
        var p = new LspPublishDiagnosticsParams("file:///y.xsl",
            [new LspDiagnostic(new LspRange(new LspPosition(3, 5), new LspPosition(3, 12)),
                1, "C", "src", "msg")]);
        var d = Assert.Single(LspProblemsBridge.Convert(p));
        Assert.NotNull(d.Location);
        Assert.Null(d.Location!.NodeId);
        Assert.Equal(4, d.Location.Line);    // 0-based 3 → 1-based 4
        Assert.Equal(6, d.Location.Column);  // 0-based 5 → 1-based 6
        Assert.Equal(7, d.Location.Length);
        Assert.Equal("file:///y.xsl", d.Location.Uri);
    }

    [Fact]
    public void CodeAndMessagePropagate()
    {
        var p = new LspPublishDiagnosticsParams("file:///z.xq",
            [new LspDiagnostic(new LspRange(new LspPosition(0, 0), new LspPosition(0, 1)),
                1, "XQST0003", "xquery", "unexpected token")]);
        var d = Assert.Single(LspProblemsBridge.Convert(p));
        Assert.Equal("XQST0003", d.Code);
        Assert.Equal("unexpected token", d.Message);
    }
}
