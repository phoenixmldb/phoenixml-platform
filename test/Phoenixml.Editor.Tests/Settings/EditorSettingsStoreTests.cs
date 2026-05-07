using System;
using System.IO;
using System.Threading.Tasks;
using Phoenixml.Editor.Settings;
using Xunit;

namespace Phoenixml.Editor.Tests.Settings;

public sealed class EditorSettingsStoreTests : IDisposable
{
    private readonly string _tempDir;

    public EditorSettingsStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"phoenixml-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best-effort cleanup — ignore locked files */ }
        GC.SuppressFinalize(this);
    }

    private string TempPath(string? suffix = null)
        => Path.Combine(_tempDir, $"settings{suffix}.json");

    // -----------------------------------------------------------------------
    // RecordRecentFile behaviour
    // -----------------------------------------------------------------------

    [Fact]
    public void RecordRecentFileAddsToFront()
    {
        var store = new EditorSettingsStore(TempPath());
        store.RecordRecentFile("/tmp/a.xml");

        Assert.Single(store.Settings.RecentFiles);
        Assert.Equal("/tmp/a.xml", store.Settings.RecentFiles[0]);
    }

    [Fact]
    public void RecordRecentFileDeduplicates()
    {
        var store = new EditorSettingsStore(TempPath());
        store.RecordRecentFile("/tmp/a.xml");
        store.RecordRecentFile("/tmp/b.xml");
        store.RecordRecentFile("/tmp/a.xml"); // should move A to front, not duplicate

        Assert.Equal(2, store.Settings.RecentFiles.Count);
        Assert.Equal("/tmp/a.xml", store.Settings.RecentFiles[0]);
        Assert.Equal("/tmp/b.xml", store.Settings.RecentFiles[1]);
    }

    [Fact]
    public void RecordRecentFileCapsAt10()
    {
        var store = new EditorSettingsStore(TempPath());
        for (var i = 0; i < 12; i++)
            store.RecordRecentFile($"/tmp/file{i}.xml");

        Assert.Equal(10, store.Settings.RecentFiles.Count);
    }

    // -----------------------------------------------------------------------
    // LoadAsync behaviour
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LoadAsyncFromMissingFileLeavesEmpty()
    {
        var store = new EditorSettingsStore(TempPath("missing"));
        // File does not exist — should complete without throwing.
        await store.LoadAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Empty(store.Settings.RecentFiles);
    }

    [Fact]
    public async Task LoadAsyncFromCorruptFileLeavesEmpty()
    {
        var path = TempPath("corrupt");
        await File.WriteAllBytesAsync(path, [0xFF, 0xFE, 0x00, 0x01],
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        var store = new EditorSettingsStore(path);
        // Should not throw; starts fresh.
        await store.LoadAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Empty(store.Settings.RecentFiles);
    }

    // -----------------------------------------------------------------------
    // Round-trip
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RoundTripPreservesRecentFiles()
    {
        var path = TempPath("roundtrip");
        var store1 = new EditorSettingsStore(path);
        store1.RecordRecentFile("/tmp/x.xml");
        store1.RecordRecentFile("/tmp/y.xml");

        // Await the deterministic save so the file exists before reload.
        await store1.SaveAsync().ConfigureAwait(true);

        var store2 = new EditorSettingsStore(path);
        await store2.LoadAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, store2.Settings.RecentFiles.Count);
        Assert.Equal("/tmp/y.xml", store2.Settings.RecentFiles[0]);
        Assert.Equal("/tmp/x.xml", store2.Settings.RecentFiles[1]);
    }
}
