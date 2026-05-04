using Microsoft.Extensions.Logging;
using Phoenixml.Platform.Hosting;

namespace Phoenixml.Platform.Diagnostics;

/// <summary>
/// A minimal rolling log file sink.  One file is created per calendar day at
/// <c>&lt;settings-root&gt;/logs/&lt;appName&gt;-yyyy-MM-dd.log</c>.
/// Each entry is a single line in the format:
/// <code>[yyyy-MM-dd HH:mm:ss.fff] [LEVEL] [category] message</code>
/// </summary>
public sealed class LogFileSink : ILogger
{
    private readonly string _appName;
    private readonly string _category;
    private readonly object _lock = new();

    /// <summary>Initialises the sink for the given application and category.</summary>
    /// <param name="appName">Application name, used to derive the log file path.</param>
    /// <param name="category">Logger category name (typically the class full name).</param>
    public LogFileSink(string appName, string category)
    {
        ArgumentNullException.ThrowIfNull(appName);
        ArgumentNullException.ThrowIfNull(category);
        _appName = appName;
        _category = category;
        EnsureLogDirectoryExists();
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var timestamp = DateTime.Now;
        var line = $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{logLevel,-11}] [{_category}] {message}";
        if (exception is not null)
            line += Environment.NewLine + exception;

        var filePath = GetLogFilePath(timestamp);
        lock (_lock)
        {
            File.AppendAllText(filePath, line + Environment.NewLine);
        }
    }

    /// <summary>Returns the log file path for the given timestamp (one file per day).</summary>
    public string GetLogFilePath(DateTime timestamp)
    {
        var dir = GetLogDirectory();
        return Path.Combine(dir, $"{_appName}-{timestamp:yyyy-MM-dd}.log");
    }

    private static string GetLogDirectory()
    {
        var root = SettingsService.GetPlatformSettingsRoot();
        return Path.Combine(root, "logs");
    }

    private static void EnsureLogDirectoryExists() =>
        Directory.CreateDirectory(GetLogDirectory());
}

/// <summary>
/// Provider that creates <see cref="LogFileSink"/> instances.
/// Register via <see cref="DiagnosticsBuilder.AddPhoenixmlDiagnostics"/>.
/// </summary>
public sealed class LogFileSinkProvider : ILoggerProvider
{
    private readonly string _appName;

    /// <summary>Initialises the provider for the given application.</summary>
    /// <param name="appName">Application name passed through to each sink instance.</param>
    public LogFileSinkProvider(string appName)
    {
        ArgumentNullException.ThrowIfNull(appName);
        _appName = appName;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) =>
        new LogFileSink(_appName, categoryName);

    /// <inheritdoc />
    public void Dispose() { /* no managed resources */ }
}
