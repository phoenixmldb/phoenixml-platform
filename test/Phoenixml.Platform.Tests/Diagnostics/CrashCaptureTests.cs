using System.Text.Json;
using Phoenixml.Platform.Diagnostics;
using Phoenixml.Platform.Hosting;
using Xunit;

namespace Phoenixml.Platform.Tests.Diagnostics;

public class CrashCaptureTests
{
    /// <summary>
    /// Tests invoke <see cref="CrashCapture.WriteCrashRecord"/> directly to exercise the
    /// serialization path without triggering an actual unhandled exception (which would
    /// terminate the test process).
    /// </summary>

    [Fact]
    public void WriteCrashRecordCreatesFile()
    {
        var appName = $"test-crash-{Guid.NewGuid():N}";
        var ex = new InvalidOperationException("test crash");

        CrashCapture.WriteCrashRecord(appName, ex, "1.0.0-test");

        var dir = Path.Combine(SettingsService.GetPlatformSettingsRoot(), "crashes");
        var files = Directory.GetFiles(dir, $"{appName}-*.json");
        Assert.NotEmpty(files);

        // Cleanup
        foreach (var f in files)
        {
            try { File.Delete(f); }
            catch (IOException) { /* ignore */ }
        }
    }

    [Fact]
    public void WriteCrashRecordProducesValidJson()
    {
        var appName = $"test-crash-{Guid.NewGuid():N}";
        var ex = new ArgumentNullException("param", "null was passed");

        CrashCapture.WriteCrashRecord(appName, ex, "2.0.0-test");

        var dir = Path.Combine(SettingsService.GetPlatformSettingsRoot(), "crashes");
        var files = Directory.GetFiles(dir, $"{appName}-*.json");
        Assert.NotEmpty(files);

        var json = File.ReadAllText(files[0]);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("ExceptionType", out var exType));
        Assert.Contains("ArgumentNullException", exType.GetString(), StringComparison.Ordinal);

        Assert.True(root.TryGetProperty("Message", out var msg));
        Assert.NotEmpty(msg.GetString()!);

        Assert.True(root.TryGetProperty("Timestamp", out _));
        Assert.True(root.TryGetProperty("StackTrace", out _));
        Assert.True(root.TryGetProperty("AppVersion", out var ver));
        Assert.Equal("2.0.0-test", ver.GetString());
        Assert.True(root.TryGetProperty("OsDescription", out var os));
        Assert.NotEmpty(os.GetString()!);

        // Cleanup
        foreach (var f in files)
        {
            try { File.Delete(f); }
            catch (IOException) { /* ignore */ }
        }
    }

    [Fact]
    public void WriteCrashRecordThrowsOnNullAppName()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CrashCapture.WriteCrashRecord(null!, new InvalidOperationException("x")));
    }

    [Fact]
    public void WriteCrashRecordThrowsOnNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CrashCapture.WriteCrashRecord("app", (Exception)null!));
    }
}
