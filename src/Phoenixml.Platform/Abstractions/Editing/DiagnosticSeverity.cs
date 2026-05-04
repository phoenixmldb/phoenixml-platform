namespace Phoenixml.Platform.Abstractions.Editing;

/// <summary>Severity level for a diagnostic reported by a validator, linter, or parser.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Informational message; no action required.</summary>
    Info,

    /// <summary>Warning; may indicate a problem but processing can continue.</summary>
    Warning,

    /// <summary>Error; processing cannot continue without resolution.</summary>
    Error
}
