using System;
using System.IO;
using Phoenixml.Editor.Find;
using Xunit;

namespace Phoenixml.Editor.Tests.Find;

public class XPathHistoryStoreTests
{
    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"phoenixml-test-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(dir, "history.txt");
            XPathHistoryStore.Save(path, ["//a", "//b", "//c"]);
            var loaded = XPathHistoryStore.Load(path);
            Assert.Equal(["//a", "//b", "//c"], loaded);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadReturnsEmptyWhenFileMissing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"phoenixml-missing-{Guid.NewGuid():N}.txt");
        var loaded = XPathHistoryStore.Load(missing);
        Assert.Empty(loaded);
    }

    [Fact]
    public void LoadSkipsBlankLines()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"phoenixml-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "history.txt");
            File.WriteAllText(path, "//a\n\n   \n//b\n");
            var loaded = XPathHistoryStore.Load(path);
            Assert.Equal(["//a", "//b"], loaded);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
