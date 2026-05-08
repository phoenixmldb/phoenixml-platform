using PhoenixmlDb.Core;

namespace Phoenixml.Platform.Abstractions.Editing;

/// <summary>
/// Read-only view of the current diagnostic-per-node state. Consumed by tree-node
/// providers to annotate rows with inline severity glyphs without taking a hard
/// dependency on the editor assembly's concrete <c>DiagnosticIndex</c> class.
/// </summary>
public interface IDiagnosticIndex
{
    /// <summary>
    /// Returns the highest <see cref="DiagnosticSeverity"/> recorded for
    /// <paramref name="id"/>, or <see langword="null"/> when the node has no diagnostics.
    /// </summary>
    /// <param name="id">The XDM node id to look up.</param>
    DiagnosticSeverity? GetSeverity(NodeId id);
}
