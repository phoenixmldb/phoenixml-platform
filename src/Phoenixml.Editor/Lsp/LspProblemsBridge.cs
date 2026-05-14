using System;
using System.Collections.Generic;
using System.Linq;
using Phoenixml.Platform.Abstractions.Editing;
using Phoenixml.Platform.Editor.Xml.Validation;

namespace Phoenixml.Editor.Lsp;

/// <summary>
/// Converts an LSP <see cref="LspPublishDiagnosticsParams"/> into editor
/// <see cref="IDiagnostic"/> records suitable for the existing
/// <c>ProblemsView</c> + <c>DiagnosticIndex</c> pipeline (Plan 27 #2).
/// </summary>
/// <remarks>
/// LSP positions are 0-based (line, character); the editor's
/// <see cref="DiagnosticLocation"/> is 1-based. We add 1 here. Text-only
/// diagnostics carry <see cref="DiagnosticLocation.NodeId"/> = null and
/// <see cref="DiagnosticLocation.Uri"/> = the document URI so click-to-navigate
/// can find the matching tab.
/// </remarks>
public static class LspProblemsBridge
{
    /// <summary>Converts each LSP diagnostic to an <see cref="IDiagnostic"/>.</summary>
    public static IReadOnlyList<IDiagnostic> Convert(LspPublishDiagnosticsParams p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return p.Diagnostics.Select(d => (IDiagnostic)new XmlDiagnostic
        {
            Severity = d.Severity switch
            {
                1 => DiagnosticSeverity.Error,
                2 => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Info,
            },
            Code = d.Code,
            Message = d.Message,
            Location = new DiagnosticLocation(
                NodeId: null,
                Line: d.Range.Start.Line + 1,
                Column: d.Range.Start.Character + 1,
                Length: Math.Max(1, d.Range.End.Character - d.Range.Start.Character),
                Uri: p.Uri),
        }).ToArray();
    }
}
