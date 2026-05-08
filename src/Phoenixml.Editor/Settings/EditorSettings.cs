namespace Phoenixml.Editor.Settings;

/// <summary>
/// Persisted editor preferences. Serialised to and from
/// <c>~/.config/phoenixml/Phoenixml.Editor/settings.json</c> (Linux) or the
/// equivalent platform path via <see cref="Phoenixml.Platform.Hosting.SettingsService"/>.
/// </summary>
internal sealed class EditorSettings
{
    /// <summary>Most-recently-opened file paths, most recent first. Capped at 10.</summary>
    public List<string> RecentFiles { get; init; } = [];

    /// <summary>
    /// Per-document XSD override paths, keyed by absolute document file path.
    /// When present, this path wins over any inline <c>xsi:schemaLocation</c> hints.
    /// </summary>
    public Dictionary<string, string> PerDocumentSchema { get; init; } = [];

    /// <summary>
    /// Persisted width of the file-explorer column in device-independent pixels.
    /// Used by the GridSplitter layout introduced in Plan 12. Default 250 dip.
    /// </summary>
    public double ExplorerWidth { get; init; } = 250;
}
