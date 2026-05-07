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
}
