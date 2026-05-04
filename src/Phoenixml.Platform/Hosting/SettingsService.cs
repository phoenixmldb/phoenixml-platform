using System.Runtime.InteropServices;
using System.Text.Json;

namespace Phoenixml.Platform.Hosting;

/// <summary>
/// Persists per-app and shared JSON settings files on disk.
/// Writes are atomic: the new content is written to a <c>.tmp</c> file then
/// renamed over the target so readers never observe a partial write.
/// </summary>
public sealed class SettingsService
{
    /// <summary>Gets the path to this application's settings file.</summary>
    public string AppSettingsPath { get; }

    /// <summary>Gets the path to the shared (cross-app) settings file.</summary>
    public string SharedSettingsPath { get; }

    /// <summary>
    /// Initialises the service and ensures the required directories exist.
    /// </summary>
    /// <param name="appName">
    /// The application name used to derive the per-app settings directory.
    /// Must not be <see langword="null"/> or empty.
    /// </param>
    public SettingsService(string appName)
    {
        ArgumentNullException.ThrowIfNull(appName);
        var basePath = GetPlatformSettingsRoot();
        AppSettingsPath = Path.Combine(basePath, appName, "settings.json");
        SharedSettingsPath = Path.Combine(basePath, "shared.json");
        Directory.CreateDirectory(Path.GetDirectoryName(AppSettingsPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(SharedSettingsPath)!);
    }

    /// <summary>
    /// Loads and deserialises a settings object from <paramref name="path"/>.
    /// Returns <see langword="null"/> if the file does not exist.
    /// </summary>
    /// <typeparam name="T">The settings type.</typeparam>
    /// <param name="path">The file path to read.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async ValueTask<T?> LoadAsync<T>(string path, CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path)) return null;
        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Serialises <paramref name="value"/> and saves it to <paramref name="path"/> atomically.
    /// The method writes to a <c>.tmp</c> file then calls <see cref="File.Move(string,string,bool)"/>
    /// to perform an atomic rename.
    /// </summary>
    /// <typeparam name="T">The settings type.</typeparam>
    /// <param name="path">The destination file path.</param>
    /// <param name="value">The value to serialise.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async ValueTask SaveAsync<T>(string path, T value, CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(value);
        var tempPath = path + ".tmp";
        var stream = File.Create(tempPath);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, value, cancellationToken: ct).ConfigureAwait(false);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>Returns the platform-canonical root directory for Phoenixml settings.</summary>
    public static string GetPlatformSettingsRoot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Phoenixml");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Phoenixml");
        // Linux + everything else
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "phoenixml");
    }
}
