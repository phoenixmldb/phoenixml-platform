using System.Collections.Generic;
using PhoenixmlDb.Core;
using Phoenixml.Platform.Abstractions.Editing;

namespace Phoenixml.Editor.Validation;

/// <summary>
/// Maps XDM <see cref="NodeId"/> values to the highest <see cref="DiagnosticSeverity"/>
/// reported for that node by the most recent validation run. Updated via
/// <see cref="Reset"/> on every <c>DiagnosticsChanged</c> event; consumed by
/// <see cref="Phoenixml.Platform.Editor.Xml.Tree.XmlTreeNodeProvider"/> to annotate
/// tree rows with inline error glyphs.
/// </summary>
internal sealed class DiagnosticIndex : IDiagnosticIndex
{
    private Dictionary<NodeId, DiagnosticSeverity> _byNode = new();

    /// <summary>
    /// Replaces the current index contents with the diagnostics from a new validation pass.
    /// For nodes that appear in multiple diagnostics, retains only the highest severity.
    /// </summary>
    /// <param name="diags">The new diagnostic list. Must not be <see langword="null"/>.</param>
    public void Reset(IReadOnlyList<IDiagnostic> diags)
    {
        ArgumentNullException.ThrowIfNull(diags);

        var fresh = new Dictionary<NodeId, DiagnosticSeverity>();
        foreach (var d in diags)
        {
            if (d.Location is not DiagnosticLocation loc) continue;
            if (!fresh.TryGetValue(loc.NodeId, out var current) || d.Severity > current)
                fresh[loc.NodeId] = d.Severity;
        }
        _byNode = fresh;
    }

    /// <inheritdoc/>
    public DiagnosticSeverity? GetSeverity(NodeId id) =>
        _byNode.TryGetValue(id, out var s) ? s : null;
}
