using System.Text.Json;
using System.Text.Json.Serialization;
using Phoenixml.Platform.Hosting;

namespace Phoenixml.Platform.Diagnostics;

/// <summary>
/// Hooks <see cref="AppDomain.UnhandledException"/> and
/// <see cref="TaskScheduler.UnobservedTaskException"/> to write a structured JSON
/// crash record to disk before the process terminates.
/// </summary>
public static class CrashCapture
{
    private static string? _appName;

    /// <summary>
    /// Installs the crash capture hooks for the given application.
    /// Call once at startup, before <c>app.Run()</c>.
    /// </summary>
    /// <param name="appName">
    /// Application name; crash records are written to
    /// <c>&lt;settings-root&gt;/crashes/&lt;appName&gt;-&lt;timestamp&gt;.json</c>.
    /// </param>
    public static void Install(string appName)
    {
        ArgumentNullException.ThrowIfNull(appName);
        _appName = appName;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// Writes a crash record to disk for the given exception.
    /// Exposed internally to allow unit tests to exercise the serialization path
    /// without triggering a real unhandled exception.
    /// </summary>
    /// <param name="appName">Application name for the crash record.</param>
    /// <param name="exception">The exception to record.</param>
    /// <param name="appVersion">Optional application version string.</param>
    internal static void WriteCrashRecord(string appName, Exception exception, string? appVersion = null)
    {
        ArgumentNullException.ThrowIfNull(appName);
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            var record = new CrashRecord(
                Timestamp: DateTimeOffset.UtcNow,
                ExceptionType: exception.GetType().FullName ?? exception.GetType().Name,
                Message: exception.Message,
                StackTrace: exception.StackTrace ?? string.Empty,
                AppVersion: appVersion ?? GetAssemblyVersion(),
                OsDescription: System.Runtime.InteropServices.RuntimeInformation.OSDescription);

            var dir = GetCrashDirectory();
            Directory.CreateDirectory(dir);
            var ts = record.Timestamp.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
            var path = Path.Combine(dir, $"{appName}-{ts}.json");
            var json = JsonSerializer.Serialize(record, CrashRecordContext.Default.CrashRecord);
            File.WriteAllText(path, json);
        }
#pragma warning disable CA1031 // Intentional: crash capture must never throw — a secondary exception would mask the original
        catch (Exception)
        {
            // Swallow — crash capture must not itself crash.
        }
#pragma warning restore CA1031
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex && _appName is not null)
            WriteCrashRecord(_appName, ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (_appName is not null)
            WriteCrashRecord(_appName, e.Exception);
        e.SetObserved();
    }

    private static string GetCrashDirectory() =>
        Path.Combine(SettingsService.GetPlatformSettingsRoot(), "crashes");

    private static string GetAssemblyVersion() =>
        typeof(CrashCapture).Assembly.GetName().Version?.ToString() ?? "unknown";
}

/// <summary>A JSON-serializable crash record.</summary>
/// <param name="Timestamp">UTC timestamp when the crash was recorded.</param>
/// <param name="ExceptionType">The fully-qualified exception type name.</param>
/// <param name="Message">The exception message.</param>
/// <param name="StackTrace">The exception stack trace.</param>
/// <param name="AppVersion">The application version string.</param>
/// <param name="OsDescription">The operating system description.</param>
public sealed record CrashRecord(
    DateTimeOffset Timestamp,
    string ExceptionType,
    string Message,
    string StackTrace,
    string AppVersion,
    string OsDescription);

/// <summary>Source-generated JSON serializer context for <see cref="CrashRecord"/>.</summary>
[JsonSerializable(typeof(CrashRecord))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class CrashRecordContext : JsonSerializerContext;
