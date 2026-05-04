using Phoenixml.Platform.Hosting;
using Xunit;

namespace Phoenixml.Platform.Tests.Hosting;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"phoenixml-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* ignore cleanup failures */ }
        GC.SuppressFinalize(this);
    }

    private sealed record SampleSettings(string Name, int Value, bool Flag);

    [Fact]
    public async Task SaveAndLoadRoundTripsValue()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        var original = new SampleSettings("hello", 42, true);

        await SettingsService.SaveAsync(path, original, CancellationToken.None);
        var loaded = await SettingsService.LoadAsync<SampleSettings>(path, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.Value, loaded.Value);
        Assert.Equal(original.Flag, loaded.Flag);
    }

    [Fact]
    public async Task SaveIsAtomicTmpFileIsReplacedWithFinal()
    {
        var path = Path.Combine(_tempDir, "atomic.json");
        var data = new SampleSettings("atomic", 1, false);

        // Write an initial version.
        await SettingsService.SaveAsync(path, data, CancellationToken.None);

        // Write a second version to the same path.
        var updated = new SampleSettings("updated", 2, true);
        await SettingsService.SaveAsync(path, updated, CancellationToken.None);

        // The .tmp file should NOT exist after the save.
        Assert.False(File.Exists(path + ".tmp"), "Temporary file should be renamed/removed after save.");

        // The final file should contain the updated value.
        var loaded = await SettingsService.LoadAsync<SampleSettings>(path, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Value);
    }

    [Fact]
    public async Task LoadReturnsNullForMissingFile()
    {
        var path = Path.Combine(_tempDir, "nonexistent.json");

        var result = await SettingsService.LoadAsync<SampleSettings>(path, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void AppSettingsPathContainsAppName()
    {
        var appName = $"test-{Guid.NewGuid():N}";
        var svc = new SettingsService(appName);
        Assert.Contains(appName, svc.AppSettingsPath, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedSettingsPathIsNotPerApp()
    {
        var appName = $"test-{Guid.NewGuid():N}";
        var svc = new SettingsService(appName);
        // The shared settings path should NOT contain the app name.
        Assert.DoesNotContain(appName, svc.SharedSettingsPath, StringComparison.Ordinal);
    }
}
