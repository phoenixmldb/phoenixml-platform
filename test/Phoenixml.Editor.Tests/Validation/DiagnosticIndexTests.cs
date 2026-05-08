using PhoenixmlDb.Core;
using Phoenixml.Editor.Validation;
using Phoenixml.Platform.Abstractions.Editing;
using Xunit;

namespace Phoenixml.Editor.Tests.Validation;

public class DiagnosticIndexTests
{
    // Minimal IDiagnostic stub for injection into Reset().
    private sealed class StubDiagnostic(
        DiagnosticSeverity severity,
        DiagnosticLocation? location) : IDiagnostic
    {
        public DiagnosticSeverity Severity { get; } = severity;
        public string? Code => null;
        public string Message => string.Empty;
        public DiagnosticLocation? Location { get; } = location;
    }

    private static DiagnosticLocation Loc(ulong nodeIdValue, int line = 1, int col = 1) =>
        new(new NodeId(nodeIdValue), line, col);

    [Fact]
    public void ResetReplacesPriorEntries()
    {
        var index = new DiagnosticIndex();
        var nodeA = new NodeId(1);
        var nodeB = new NodeId(2);

        // First pass: nodeA = Error.
        index.Reset(new IDiagnostic[]
        {
            new StubDiagnostic(DiagnosticSeverity.Error, Loc(nodeA.Value)),
        });
        Assert.Equal(DiagnosticSeverity.Error, index.GetSeverity(nodeA));

        // Second pass: nodeA not present, nodeB = Warning. Prior entry for nodeA is gone.
        index.Reset(new IDiagnostic[]
        {
            new StubDiagnostic(DiagnosticSeverity.Warning, Loc(nodeB.Value)),
        });
        Assert.Null(index.GetSeverity(nodeA));
        Assert.Equal(DiagnosticSeverity.Warning, index.GetSeverity(nodeB));
    }

    [Fact]
    public void GetSeverityReturnsHighestSeverityForDuplicates()
    {
        var index = new DiagnosticIndex();
        var nodeId = new NodeId(10);

        // Two diagnostics for the same node: Warning first, then Error.
        // The index must keep only Error (the higher severity).
        index.Reset(new IDiagnostic[]
        {
            new StubDiagnostic(DiagnosticSeverity.Warning, Loc(nodeId.Value)),
            new StubDiagnostic(DiagnosticSeverity.Error,   Loc(nodeId.Value)),
        });

        Assert.Equal(DiagnosticSeverity.Error, index.GetSeverity(nodeId));
    }

    [Fact]
    public void GetSeverityReturnsNullForUnknownNodeId()
    {
        var index = new DiagnosticIndex();
        var known   = new NodeId(5);
        var unknown = new NodeId(99);

        index.Reset(new IDiagnostic[]
        {
            new StubDiagnostic(DiagnosticSeverity.Info, Loc(known.Value)),
        });

        Assert.Null(index.GetSeverity(unknown));
    }
}
