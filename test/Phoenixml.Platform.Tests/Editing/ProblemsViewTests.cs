using Phoenixml.Platform.Abstractions.Editing;
using Xunit;

namespace Phoenixml.Platform.Tests.Editing;

/// <summary>Minimal <see cref="IDiagnostic"/> implementation for ProblemsView tests.</summary>
file sealed class SimpleDiagnostic(
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

public class ProblemsViewTests
{
    [Fact]
    public void SetDiagnosticsAcceptsEmptyList()
    {
        // Empty list must not throw and yields zero rows.
        var view = new Phoenixml.Platform.Editing.ProblemsView();
        view.SetDiagnostics(Array.Empty<IDiagnostic>());
        // No exception = pass.
    }

    [Fact]
    public void SetDiagnosticsAcceptsMixedSeverities()
    {
        var view = new Phoenixml.Platform.Editing.ProblemsView();
        var diagnostics = new IDiagnostic[]
        {
            new SimpleDiagnostic(DiagnosticSeverity.Info,    "An info message"),
            new SimpleDiagnostic(DiagnosticSeverity.Warning, "A warning",   code: "W001"),
            new SimpleDiagnostic(DiagnosticSeverity.Error,   "An error",    code: "E001")
        };
        view.SetDiagnostics(diagnostics); // must not throw
    }

    [Fact]
    public void SetDiagnosticsNullThrows()
    {
        var view = new Phoenixml.Platform.Editing.ProblemsView();
        Assert.Throws<ArgumentNullException>(() => view.SetDiagnostics(null!));
    }
}
