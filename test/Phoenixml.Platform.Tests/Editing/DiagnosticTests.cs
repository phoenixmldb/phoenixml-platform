using PhoenixmlDb.Core;
using Phoenixml.Platform.Abstractions.Editing;
using Xunit;

namespace Phoenixml.Platform.Tests.Editing;

/// <summary>Minimal concrete <see cref="IDiagnostic"/> for testing.</summary>
file sealed class TestDiagnostic(
    DiagnosticSeverity severity,
    string message,
    string? code = null,
    DiagnosticLocation? location = null) : IDiagnostic
{
    public DiagnosticSeverity Severity { get; } = severity;
    public string? Code { get; } = code;
    public string Message { get; } = message;
    public DiagnosticLocation? Location { get; } = location;
}

public class DiagnosticTests
{
    [Fact]
    public void SeverityOrderingIsInfoLessThanWarningLessThanError()
    {
        // Enum values must follow ascending severity order.
        Assert.True((int)DiagnosticSeverity.Info < (int)DiagnosticSeverity.Warning);
        Assert.True((int)DiagnosticSeverity.Warning < (int)DiagnosticSeverity.Error);
    }

    [Fact]
    public void InfoDiagnosticCarriesSeverity()
    {
        var d = new TestDiagnostic(DiagnosticSeverity.Info, "Just an info.");
        Assert.Equal(DiagnosticSeverity.Info, d.Severity);
    }

    [Fact]
    public void WarningDiagnosticCarriesSeverity()
    {
        var d = new TestDiagnostic(DiagnosticSeverity.Warning, "A warning.");
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
    }

    [Fact]
    public void ErrorDiagnosticCarriesSeverity()
    {
        var d = new TestDiagnostic(DiagnosticSeverity.Error, "An error.");
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void CodeIsNullWhenNotProvided()
    {
        var d = new TestDiagnostic(DiagnosticSeverity.Info, "msg");
        Assert.Null(d.Code);
    }

    [Fact]
    public void CodeIsPreservedWhenProvided()
    {
        var d = new TestDiagnostic(DiagnosticSeverity.Error, "msg", code: "XSD0001");
        Assert.Equal("XSD0001", d.Code);
    }

    [Fact]
    public void LocationIsNullWhenNotProvided()
    {
        var d = new TestDiagnostic(DiagnosticSeverity.Warning, "msg");
        Assert.Null(d.Location);
    }

    [Fact]
    public void LocationIsPreservedWhenProvided()
    {
        var loc = new DiagnosticLocation(new NodeId(42), Line: 5, Column: 3);
        var d = new TestDiagnostic(DiagnosticSeverity.Error, "msg", location: loc);
        Assert.Equal(loc, d.Location);
        Assert.Equal(new NodeId(42), d.Location!.NodeId);
        Assert.Equal(5, d.Location.Line);
        Assert.Equal(3, d.Location.Column);
    }

    [Fact]
    public void SortedBySeverityProducesInfoWarningError()
    {
        var diagnostics = new TestDiagnostic[]
        {
            new(DiagnosticSeverity.Error, "e"),
            new(DiagnosticSeverity.Info, "i"),
            new(DiagnosticSeverity.Warning, "w")
        };

        var sorted = diagnostics.OrderBy(d => d.Severity).ToList();

        Assert.Equal(DiagnosticSeverity.Info, sorted[0].Severity);
        Assert.Equal(DiagnosticSeverity.Warning, sorted[1].Severity);
        Assert.Equal(DiagnosticSeverity.Error, sorted[2].Severity);
    }
}
