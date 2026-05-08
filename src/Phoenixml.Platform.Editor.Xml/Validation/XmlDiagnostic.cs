using Phoenixml.Platform.Abstractions.Editing;

namespace Phoenixml.Platform.Editor.Xml.Validation;

/// <summary>
/// <see cref="IDiagnostic"/> for an XML validation finding. <see cref="Location"/> is
/// populated with the closest XDM node whose parse-time source position matches the
/// reported line/column, or <see langword="null"/> for structural errors without a
/// node anchor.
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
    public DiagnosticLocation? Location { get; init; }
}
