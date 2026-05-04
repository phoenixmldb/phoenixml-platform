using Microsoft.Extensions.Logging;
using Phoenixml.Platform.Diagnostics;
using Xunit;

namespace Phoenixml.Platform.Tests.Diagnostics;

public sealed class LogFileSinkTests : IDisposable
{
    // We cannot easily redirect the sink's output directory without modifying
    // production code. Instead we test the public API surface: that the sink
    // implements ILogger correctly and writes entries to a predictable path.
    // The actual file will land in the real log directory; we verify content.

    private readonly string _appName = $"test-{Guid.NewGuid():N}";
    private readonly LogFileSink _sink;

    public LogFileSinkTests()
    {
        _sink = new LogFileSink(_appName, "TestCategory");
    }

    public void Dispose()
    {
        // Best-effort cleanup of any log files written by this test run.
        var logPath = _sink.GetLogFilePath(DateTime.Now);
        try { File.Delete(logPath); }
        catch (IOException) { /* ignore */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void IsEnabledReturnsTrueForNonNoneLogLevels()
    {
        Assert.True(_sink.IsEnabled(LogLevel.Trace));
        Assert.True(_sink.IsEnabled(LogLevel.Debug));
        Assert.True(_sink.IsEnabled(LogLevel.Information));
        Assert.True(_sink.IsEnabled(LogLevel.Warning));
        Assert.True(_sink.IsEnabled(LogLevel.Error));
        Assert.True(_sink.IsEnabled(LogLevel.Critical));
    }

    [Fact]
    public void IsEnabledReturnsFalseForNone()
    {
        Assert.False(_sink.IsEnabled(LogLevel.None));
    }

    [Fact]
    public void LogWritesFileWithContent()
    {
        _sink.Log(LogLevel.Information, default, "test message", null,
            (state, _) => state);

        var logPath = _sink.GetLogFilePath(DateTime.Now);
        Assert.True(File.Exists(logPath), $"Log file should exist at {logPath}.");
        var content = File.ReadAllText(logPath);
        Assert.Contains("test message", content, StringComparison.Ordinal);
    }

    [Fact]
    public void LogIncludesLogLevelInOutput()
    {
        _sink.Log(LogLevel.Warning, default, "warning entry", null,
            (state, _) => state);

        var logPath = _sink.GetLogFilePath(DateTime.Now);
        Assert.True(File.Exists(logPath));
        var content = File.ReadAllText(logPath);
        Assert.Contains("Warning", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LogIncludesExceptionDetailsWhenPresent()
    {
        var ex = new InvalidOperationException("boom");
        _sink.Log(LogLevel.Error, default, "error with exception", ex,
            (state, _) => state);

        var logPath = _sink.GetLogFilePath(DateTime.Now);
        var content = File.ReadAllText(logPath);
        Assert.Contains("InvalidOperationException", content, StringComparison.Ordinal);
    }

    [Fact]
    public void GetLogFilePathRollsPerDay()
    {
        var today = new DateTime(2026, 5, 3);
        var tomorrow = today.AddDays(1);

        var pathToday = _sink.GetLogFilePath(today);
        var pathTomorrow = _sink.GetLogFilePath(tomorrow);

        Assert.NotEqual(pathToday, pathTomorrow);
        Assert.Contains("2026-05-03", pathToday, StringComparison.Ordinal);
        Assert.Contains("2026-05-04", pathTomorrow, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorThrowsOnNullAppName()
    {
        Assert.Throws<ArgumentNullException>(() => new LogFileSink(null!, "cat"));
    }

    [Fact]
    public void ConstructorThrowsOnNullCategory()
    {
        Assert.Throws<ArgumentNullException>(() => new LogFileSink("app", null!));
    }
}
