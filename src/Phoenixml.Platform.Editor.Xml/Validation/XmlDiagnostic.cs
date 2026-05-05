using Phoenixml.Platform.Abstractions.Editing;

namespace Phoenixml.Platform.Editor.Xml.Validation;

/// <summary>
/// <see cref="IDiagnostic"/> for an XML validation finding. Location payload is the
/// offending XdmNode (or null when the error is structural and lacks a node anchor).
/// </summary>
public sealed class XmlDiagnostic : IDiagnostic
{
    /// <inheritdoc/>
    public required DiagnosticSeverity Severity { get; init; }

    /// <inheritdoc/>
    public string? Code { get; init; }

    /// <inheritdoc/>
    public required string Message { get; init; }

    /// <inheritdoc/>
    public object? Location { get; init; }
}
