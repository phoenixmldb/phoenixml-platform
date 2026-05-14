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
/// Associates a diagnostic with a position. For XML diagnostics, <see cref="NodeId"/>
/// identifies the matching XDM node. For text-only diagnostics (LSP from XQuery/XSLT),
/// <see cref="NodeId"/> is <see langword="null"/> and <see cref="Uri"/> +
/// (<see cref="Line"/>, <see cref="Column"/>) identify the source position.
/// </summary>
/// <param name="NodeId">XDM node id, or null for text-only diagnostics.</param>
/// <param name="Line">One-based line number.</param>
/// <param name="Column">One-based column number.</param>
/// <param name="Length">Span length for diagnostic squiggles (default 1).</param>
/// <param name="Uri">Absolute document URI; used by ProblemsView grouping + click-to-navigate.</param>
public sealed record DiagnosticLocation(
    NodeId? NodeId,
    int Line,
    int Column,
    int Length = 1,
    string? Uri = null);
