using PhoenixmlDb.Core;

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
    /// Structured location tying this diagnostic to an XDM node and its source
    /// coordinates. Null when the error is structural and lacks a node anchor
    /// (e.g. a document-level well-formedness failure).
    /// </summary>
    DiagnosticLocation? Location { get; }
}

/// <summary>
/// Associates a diagnostic with a specific XDM node by its <see cref="NodeId"/>,
/// plus the parse-time line and column from the serialized form that triggered
/// the validation error.
/// </summary>
/// <param name="NodeId">The XDM node id of the closest matching node.</param>
/// <param name="Line">One-based line number in the serialized document.</param>
/// <param name="Column">One-based column number in the serialized document.</param>
public sealed record DiagnosticLocation(NodeId NodeId, int Line, int Column);
