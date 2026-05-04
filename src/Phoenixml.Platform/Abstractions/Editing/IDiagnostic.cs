namespace Phoenixml.Platform.Abstractions.Editing;

/// <summary>
/// A single problem reported by a validator, linter, or parser. Surfaces in
/// <c>ProblemsView</c>; click-to-navigate uses <see cref="Location"/>.
/// </summary>
public interface IDiagnostic
{
    /// <summary>Severity level of this diagnostic.</summary>
    DiagnosticSeverity Severity { get; }

    /// <summary>Diagnostic code (e.g. "XSD0001"). Null = uncoded.</summary>
    string? Code { get; }

    /// <summary>Human-readable message.</summary>
    string Message { get; }

    /// <summary>
    /// Document location reference. Opaque to the view — the view passes it back to
    /// the document on click. Document interprets it (line/col, XPath, XDM-id, etc.).
    /// </summary>
    object? Location { get; }
}
