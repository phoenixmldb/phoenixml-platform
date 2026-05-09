using System.Text.Json;
using Phoenixml.Platform.Hosting;

namespace Phoenixml.Editor.Settings;

/// <summary>
/// Loads and saves <see cref="EditorSettings"/> to the platform settings file.
/// In-memory mutations fire <see cref="Changed"/> so menus can refresh.
/// Save runs in the background; failures are swallowed so they never crash the editor.
/// </summary>
internal sealed class EditorSettingsStore
{
    private const int RecentFilesCap = 10;

    private readonly string _settingsPath;
    private EditorSettings _settings = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    /// <summary>
    /// Initialises the store using the default platform settings path for
    /// <c>Phoenixml.Editor</c>.
    /// </summary>
    public EditorSettingsStore()
        : this(new SettingsService("Phoenixml.Editor").AppSettingsPath) { }

    /// <summary>
    /// Initialises the store using an explicit settings file path.
    /// Intended for unit tests that need an isolated, temporary path.
    /// </summary>
    /// <param name="settingsPath">Absolute path to the settings JSON file.</param>
    internal EditorSettingsStore(string settingsPath)
    {
        ArgumentNullException.ThrowIfNull(settingsPath);
        _settingsPath = settingsPath;
    }

    /// <summary>Gets the current in-memory settings snapshot.</summary>
    public EditorSettings Settings => _settings;

    /// <summary>
    /// Raised after <see cref="LoadAsync"/> completes and after each
    /// <see cref="RecordRecentFile"/> call so subscribers (e.g. menus) can refresh.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Loads settings from disk. Tolerates a missing file (starts fresh) or a corrupt
    /// file (starts fresh). Always raises <see cref="Changed"/> on completion.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            var loaded = await SettingsService.LoadAsync<EditorSettings>(_settingsPath, ct)
                .ConfigureAwait(false);
            if (loaded is not null)
                _settings = loaded;
        }
        catch (IOException) { /* missing or locked — start fresh */ }
        catch (JsonException) { /* corrupt JSON — start fresh */ }

        Changed?.Invoke();
    }

    /// <summary>
    /// Adds <paramref name="path"/> to the front of <see cref="EditorSettings.RecentFiles"/>,
    /// deduplicating and capping at 10. Raises <see cref="Changed"/> and starts a
    /// fire-and-forget background save.
    /// </summary>
    /// <param name="path">Absolute file path to record.</param>
    public void RecordRecentFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var list = _settings.RecentFiles;
        list.Remove(path);          // dedupe: remove existing occurrence anywhere in the list
        list.Insert(0, path);       // most-recent-first
        while (list.Count > RecentFilesCap)
            list.RemoveAt(list.Count - 1);

        Changed?.Invoke();
        _ = SaveAsync();            // fire-and-forget
    }

    /// <summary>
    /// Updates the persisted file-explorer column width. Sub-pixel changes (less than
    /// 1 dip) are ignored to avoid spurious saves on every drag tick.
    /// Raises <see cref="Changed"/> and starts a fire-and-forget background save.
    /// </summary>
    /// <param name="width">New column width in device-independent pixels.</param>
    public void UpdateExplorerWidth(double width)
    {
        if (Math.Abs(width - _settings.ExplorerWidth) < 1) return;

        _settings = new EditorSettings
        {
            RecentFiles = _settings.RecentFiles,
            PerDocumentSchema = _settings.PerDocumentSchema,
            ExplorerWidth = width,
        };
        Changed?.Invoke();
        _ = SaveAsync();            // fire-and-forget
    }

    /// <summary>
    /// Persists the current settings to disk. Can be awaited directly in tests.
    /// </summary>
    internal async Task SaveAsync()
    {
        // Serialize concurrent saves so a fire-and-forget call from UpdateExplorerWidth
        // (or RecordRecentFile) doesn't collide with another in-flight save on the
        // same .tmp file. Without this, two writes can race on the temporary file and
        // File.Move can silently lose the loser's content.
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Ensure the directory exists (relevant when using an injected test path).
            var dir = Path.GetDirectoryName(_settingsPath);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            await SettingsService.SaveAsync(_settingsPath, _settings, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally
        {
            _saveLock.Release();
        }
    }
}
