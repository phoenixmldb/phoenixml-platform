using System;
using System.IO;
using System.Xml.Schema;
using Phoenixml.Editor.Validation;
using Xunit;

namespace Phoenixml.Editor.Tests.Validation;

/// <summary>
/// Tests for <see cref="SchemaCache"/>.
/// </summary>
public sealed class SchemaCacheTests : IDisposable
{
    private readonly string _tempDir;

    /// <summary>A minimal valid XSD: declares a single <c>root</c> string element.</summary>
    private const string SimpleXsd = """
        <?xml version="1.0"?>
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="root" type="xs:string"/>
        </xs:schema>
        """;

    public SchemaCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"phoenixml-cache-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best-effort cleanup */ }
        GC.SuppressFinalize(this);
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void GetOrLoadFromMissingFileReturnsNull()
    {
        var cache = new SchemaCache();
        var missingPath = Path.Combine(_tempDir, "does-not-exist.xsd");

        var result = cache.GetOrLoad(missingPath);

        Assert.Null(result);
    }

    [Fact]
    public void GetOrLoadCacheHitReturnsSameInstance()
    {
        var xsdPath = WriteFile("schema.xsd", SimpleXsd);
        var cache = new SchemaCache();

        var first = cache.GetOrLoad(xsdPath);
        var second = cache.GetOrLoad(xsdPath);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrLoadAfterFileMtimeChangeReturnsFreshInstance()
    {
        var xsdPath = WriteFile("mtime-schema.xsd", SimpleXsd);
        var cache = new SchemaCache();

        var first = cache.GetOrLoad(xsdPath);
        Assert.NotNull(first);

        // Advance the mtime so the cache key changes.
        File.SetLastWriteTimeUtc(xsdPath, File.GetLastWriteTimeUtc(xsdPath).AddSeconds(1));

        var second = cache.GetOrLoad(xsdPath);
        Assert.NotNull(second);

        Assert.NotSame(first, second);
    }

    [Fact]
    public void GetOrLoadCorruptFileReturnsNull()
    {
        var xsdPath = WriteFile("corrupt.xsd", "this is not valid XML or XSD content");
        var cache = new SchemaCache();

        var result = cache.GetOrLoad(xsdPath);

        Assert.Null(result);
    }

    [Fact]
    public void ClearEvictsAll()
    {
        var xsdPath = WriteFile("evict-schema.xsd", SimpleXsd);
        var cache = new SchemaCache();

        var first = cache.GetOrLoad(xsdPath);
        Assert.NotNull(first);

        cache.Clear();

        var second = cache.GetOrLoad(xsdPath);
        Assert.NotNull(second);

        // After Clear the cache is empty; second load creates a fresh instance.
        Assert.NotSame(first, second);
    }
}
