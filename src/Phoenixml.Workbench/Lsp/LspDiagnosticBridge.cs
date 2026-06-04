using System.Collections.Generic;
using System.Linq;

namespace Phoenixml.Workbench.Lsp;

/// <summary>
/// One LSP diagnostic mapped into a flat editor-friendly shape. Carries 0-based
/// line/column (LSP convention), severity (1=Error, 2=Warning, 3=Info, 4=Hint),
/// message + optional code + source. Surfaced by <c>EditorMainWindow</c> via
/// <c>NotifyAction</c> in MVP; richer Problems-pane integration is Plan 27.
/// </summary>
public sealed record EditorLspDiagnostic(
    string Uri,
    int Line,
    int Column,
    int Length,
    int Severity,
    string? Code,
    string? Source,
    string Message);

/// <summary>
/// Converts LSP <see cref="LspPublishDiagnosticsParams"/> into <see cref="EditorLspDiagnostic"/>s.
/// Pure function; no host dependencies.
/// </summary>
public static class LspDiagnosticBridge
{
    /// <summary>Maps each LSP diagnostic to an editor-friendly record.</summary>
    public static IReadOnlyList<EditorLspDiagnostic> Convert(LspPublishDiagnosticsParams p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return p.Diagnostics.Select(d => new EditorLspDiagnostic(
            Uri: p.Uri,
            Line: d.Range.Start.Line,
            Column: d.Range.Start.Character,
            Length: System.Math.Max(0, d.Range.End.Character - d.Range.Start.Character),
            Severity: d.Severity,
            Code: d.Code,
            Source: d.Source,
            Message: d.Message)).ToArray();
    }
}
