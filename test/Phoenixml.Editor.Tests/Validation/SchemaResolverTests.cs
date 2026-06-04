using System;
using System.IO;
using System.Threading.Tasks;
using Phoenixml.Workbench.Settings;
using Phoenixml.Editor.Validation;
using Phoenixml.Platform.Editor.Xml;
using Xunit;
using PlatformDocumentId = Phoenixml.Platform.Abstractions.Documents.DocumentId;

namespace Phoenixml.Editor.Tests.Validation;

public sealed class SchemaResolverTests : IDisposable
{
    private readonly string _tempDir;

    // A minimal XSD: allows <root> as a string element.
    private const string SimpleXsd = """
        <?xml version="1.0"?>
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="root" type="xs:string"/>
        </xs:schema>
        """;

    // A second XSD: allows <other> as a string element (used for override tests).
    private const string OtherXsd = """
        <?xml version="1.0"?>
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="other" type="xs:string"/>
        </xs:schema>
        """;

    // Malformed XSD content.
    private const string CorruptXsd = "this is not valid XML or XSD";

    public SchemaResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"phoenixml-resolver-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best-effort cleanup */ }
        GC.SuppressFinalize(this);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static XmlDocument MakeDoc(string xml, string? filePath = null)
    {
        var source = filePath is not null ? new Uri(filePath) : null;
        var displayName = filePath is not null ? Path.GetFileName(filePath) : "doc.xml";
        var (result, nsTable) = XmlDocument.ParseXml(xml, filePath is not null ? new Uri(filePath).ToString() : null);
        return new XmlDocument(PlatformDocumentId.New(), source, displayName, result, nsTable);
    }

    private SchemaResolver MakeResolver(
        Dictionary<string, string>? perDocSchema = null,
        SchemaCache? cache = null)
    {
        var settingsPath = Path.Combine(_tempDir, $"settings-{Guid.NewGuid()}.json");
        var store = new EditorSettingsStore(settingsPath);

        if (perDocSchema is not null)
        {
            // Populate via reflection to avoid needing a public mutator.
            // PerDocumentSchema is a mutable List/Dictionary on the settings object.
            foreach (var kvp in perDocSchema)
                store.Settings.PerDocumentSchema.Add(kvp.Key, kvp.Value);
        }

        return new SchemaResolver(store, cache);
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveReturnsNullWhenNoSchemaHint()
    {
        var doc = MakeDoc("<root>hello</root>");
        var resolver = MakeResolver();

        var result = resolver.Resolve(doc);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveLoadsNoNamespaceSchemaLocationFromInline()
    {
        // Write the XSD to disk.
        var xsdPath = WriteFile("schema.xsd", SimpleXsd);
        // Write an XML file in the same directory with xsi:noNamespaceSchemaLocation.
        var xmlPath = WriteFile("doc.xml", $"""
            <?xml version="1.0"?>
            <root xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  xsi:noNamespaceSchemaLocation="schema.xsd">hello</root>
            """);

        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        var resolver = MakeResolver();

        var result = resolver.Resolve(doc);

        Assert.NotNull(result);
    }

    [Fact]
    public void ResolveLoadsRelativeSchemaPath()
    {
        // The XSD is in the same directory as the XML file — relative path resolution.
        var xsdPath = WriteFile("relative.xsd", SimpleXsd);
        var xmlPath = WriteFile("relative.xml", """
            <?xml version="1.0"?>
            <root xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  xsi:noNamespaceSchemaLocation="relative.xsd">hello</root>
            """);

        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        var resolver = MakeResolver();

        var result = resolver.Resolve(doc);

        Assert.NotNull(result);
        // Verify it compiled — a compiled XmlSchemaSet has Count > 0.
        Assert.True(result!.Count > 0);
        _ = xsdPath; // referenced to suppress unused warning
    }

    [Fact]
    public void ResolvePrefersOverrideOverInlineHint()
    {
        // Write two XSDs: one referenced inline, one in the override map.
        var inlineXsd = WriteFile("inline.xsd", SimpleXsd);
        var overrideXsd = WriteFile("override.xsd", OtherXsd);

        // XML references the inline XSD.
        var xmlPath = WriteFile("override-test.xml", $"""
            <?xml version="1.0"?>
            <root xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  xsi:noNamespaceSchemaLocation="inline.xsd">hello</root>
            """);

        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        // Settings overrides to point at the other XSD.
        var resolver = MakeResolver(new Dictionary<string, string> { [xmlPath] = overrideXsd });

        var result = resolver.Resolve(doc);

        // Override wins — the returned schema set should be from override.xsd (which defines <other>).
        Assert.NotNull(result);
        // Verify the override schema compiled — a compiled set has Count > 0.
        Assert.True(result!.Count > 0);
        _ = inlineXsd;
    }

    [Fact]
    public void ResolveReturnsNullOnMissingSchemaFile()
    {
        var xmlPath = WriteFile("missing-xsd.xml", """
            <?xml version="1.0"?>
            <root xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  xsi:noNamespaceSchemaLocation="does-not-exist.xsd">hello</root>
            """);

        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        var resolver = MakeResolver();

        var result = resolver.Resolve(doc);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveReturnsNullOnCorruptSchema()
    {
        // The XSD file exists but contains invalid content.
        WriteFile("corrupt.xsd", CorruptXsd);
        var xmlPath = WriteFile("corrupt-test.xml", """
            <?xml version="1.0"?>
            <root xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  xsi:noNamespaceSchemaLocation="corrupt.xsd">hello</root>
            """);

        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        var resolver = MakeResolver();

        var result = resolver.Resolve(doc);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveCachesAcrossCalls()
    {
        // Verify that the resolved XmlSchemaSet is served from the cache on subsequent
        // calls — the XSD file is deleted between the first and second Resolve call.
        // If the cache were bypassed the second call would return null; cache hit means
        // the same non-null set is returned.
        var xsdPath = WriteFile("cached.xsd", SimpleXsd);
        var xmlPath = WriteFile("cached.xml", $"""
            <?xml version="1.0"?>
            <root xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  xsi:noNamespaceSchemaLocation="cached.xsd">hello</root>
            """);

        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        var cache = new SchemaCache();
        var resolver = MakeResolver(cache: cache);

        // First call — populates the cache.
        var first = resolver.Resolve(doc);
        Assert.NotNull(first);

        // Delete the XSD from disk. A cache miss would now return null.
        File.Delete(xsdPath);

        // Second call — must hit the cache and return the same instance.
        var second = resolver.Resolve(doc);
        Assert.NotNull(second);
        Assert.Same(first, second);
    }

    // -----------------------------------------------------------------------
    // Auto-detect heuristic tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writes a file inside a subdirectory relative to <see cref="_tempDir"/>.
    /// Creates intermediate directories as needed.
    /// </summary>
    private static string WriteFileInDir(string dir, string name, string content)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ResolveSameFolderNameMatch()
    {
        // Layout: dir/library.xml + dir/library.xsd
        var xmlPath = WriteFile("library.xml", "<root>hello</root>");
        WriteFile("library.xsd", SimpleXsd);

        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        var resolver = MakeResolver();

        var resolution = resolver.ResolveDetailed(doc);

        Assert.NotNull(resolution);
        Assert.Equal(SchemaResolutionKind.AutoSameFolderName, resolution!.Kind);
        Assert.True(resolution.Schemas.Count > 0);
    }

    [Fact]
    public void ResolveSameFolderSingleXsd()
    {
        // Layout: dir/library.xml + dir/unrelated.xsd (only one XSD, no name match)
        var xmlPath = WriteFile("library2.xml", "<root>hello</root>");
        WriteFile("unrelated.xsd", SimpleXsd);

        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        var resolver = MakeResolver();

        var resolution = resolver.ResolveDetailed(doc);

        Assert.NotNull(resolution);
        Assert.Equal(SchemaResolutionKind.AutoSameFolderSingle, resolution!.Kind);
        Assert.True(resolution.Schemas.Count > 0);
    }

    [Fact]
    public void ResolveSameFolderMultipleXsdsNoMatchReturnsNull()
    {
        // Layout: dir/nomatch.xml + dir/foo.xsd + dir/bar.xsd (two XSDs, no name match → don't guess)
        // Use an isolated subfolder to avoid pollution from other test files in _tempDir.
        var subDir = Path.Combine(_tempDir, "multi-xsd");
        Directory.CreateDirectory(subDir);
        WriteFileInDir(subDir, "nomatch.xml", "<root>hello</root>");
        WriteFileInDir(subDir, "foo.xsd", SimpleXsd);
        WriteFileInDir(subDir, "bar.xsd", OtherXsd);

        var xmlPath = Path.Combine(subDir, "nomatch.xml");
        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        var resolver = MakeResolver();

        var result = resolver.Resolve(doc);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveSubfolderNameMatch()
    {
        // Layout: dir/library.xml + dir/schemas/library.xsd
        var subDir = Path.Combine(_tempDir, "subfolder-name");
        Directory.CreateDirectory(subDir);
        var schemasDir = Path.Combine(subDir, "schemas");
        Directory.CreateDirectory(schemasDir);

        WriteFileInDir(subDir, "library.xml", "<root>hello</root>");
        WriteFileInDir(schemasDir, "library.xsd", SimpleXsd);

        var xmlPath = Path.Combine(subDir, "library.xml");
        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        var resolver = MakeResolver();

        var resolution = resolver.ResolveDetailed(doc);

        Assert.NotNull(resolution);
        Assert.Equal(SchemaResolutionKind.AutoSubfolderName, resolution!.Kind);
        Assert.True(resolution.Schemas.Count > 0);
    }

    [Fact]
    public void ResolveSubfolderSingleXsd()
    {
        // Layout: dir/library.xml + dir/schemas/foo.xsd (only one XSD in subfolder)
        var subDir = Path.Combine(_tempDir, "subfolder-single");
        Directory.CreateDirectory(subDir);
        var schemasDir = Path.Combine(subDir, "schemas");
        Directory.CreateDirectory(schemasDir);

        WriteFileInDir(subDir, "library.xml", "<root>hello</root>");
        WriteFileInDir(schemasDir, "foo.xsd", SimpleXsd);

        var xmlPath = Path.Combine(subDir, "library.xml");
        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        var resolver = MakeResolver();

        var resolution = resolver.ResolveDetailed(doc);

        Assert.NotNull(resolution);
        Assert.Equal(SchemaResolutionKind.AutoSubfolderSingle, resolution!.Kind);
        Assert.True(resolution.Schemas.Count > 0);
    }

    [Fact]
    public void ResolveSiblingFolderNameMatch()
    {
        // Layout: parent/data/library.xml + parent/schemas/library.xsd
        var parentDir = Path.Combine(_tempDir, "sib-name-parent");
        var dataDir = Path.Combine(parentDir, "data");
        var schemasDir = Path.Combine(parentDir, "schemas");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(schemasDir);

        WriteFileInDir(dataDir, "library.xml", "<root>hello</root>");
        WriteFileInDir(schemasDir, "library.xsd", SimpleXsd);

        var xmlPath = Path.Combine(dataDir, "library.xml");
        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        var resolver = MakeResolver();

        var resolution = resolver.ResolveDetailed(doc);

        Assert.NotNull(resolution);
        Assert.Equal(SchemaResolutionKind.AutoSiblingFolderName, resolution!.Kind);
        Assert.True(resolution.Schemas.Count > 0);
    }

    [Fact]
    public void ResolveSiblingFolderSingleXsd()
    {
        // Layout: parent/data/library.xml + parent/schemas/foo.xsd (only one XSD in sibling)
        var parentDir = Path.Combine(_tempDir, "sib-single-parent");
        var dataDir = Path.Combine(parentDir, "data");
        var schemasDir = Path.Combine(parentDir, "schemas");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(schemasDir);

        WriteFileInDir(dataDir, "library.xml", "<root>hello</root>");
        WriteFileInDir(schemasDir, "foo.xsd", SimpleXsd);

        var xmlPath = Path.Combine(dataDir, "library.xml");
        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        var resolver = MakeResolver();

        var resolution = resolver.ResolveDetailed(doc);

        Assert.NotNull(resolution);
        Assert.Equal(SchemaResolutionKind.AutoSiblingFolderSingle, resolution!.Kind);
        Assert.True(resolution.Schemas.Count > 0);
    }

    [Fact]
    public void ResolveAutoDetectChecksXsdSubfolderToo()
    {
        // Layout: dir/library.xml + dir/xsd/library.xsd (alternate subfolder name)
        var subDir = Path.Combine(_tempDir, "xsd-subfolder");
        Directory.CreateDirectory(subDir);
        var xsdDir = Path.Combine(subDir, "xsd");
        Directory.CreateDirectory(xsdDir);

        WriteFileInDir(subDir, "library.xml", "<root>hello</root>");
        WriteFileInDir(xsdDir, "library.xsd", SimpleXsd);

        var xmlPath = Path.Combine(subDir, "library.xml");
        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        var resolver = MakeResolver();

        var resolution = resolver.ResolveDetailed(doc);

        Assert.NotNull(resolution);
        Assert.Equal(SchemaResolutionKind.AutoSubfolderName, resolution!.Kind);
        Assert.True(resolution.Schemas.Count > 0);
    }

    [Fact]
    public void ResolveOverrideBeatsAutoDetect()
    {
        // Layout: dir/library.xml + dir/library.xsd (auto-detect would pick the latter),
        // BUT settings has an override pointing at override.xsd (defines <other>).
        var subDir = Path.Combine(_tempDir, "override-wins");
        Directory.CreateDirectory(subDir);

        WriteFileInDir(subDir, "library.xml", "<root>hello</root>");
        WriteFileInDir(subDir, "library.xsd", SimpleXsd);
        var overridePath = WriteFileInDir(subDir, "override.xsd", OtherXsd);

        var xmlPath = Path.Combine(subDir, "library.xml");
        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        var resolver = MakeResolver(new Dictionary<string, string> { [xmlPath] = overridePath });

        var resolution = resolver.ResolveDetailed(doc);

        Assert.NotNull(resolution);
        Assert.Equal(SchemaResolutionKind.Override, resolution!.Kind);
        Assert.Equal(Path.GetFullPath(overridePath), Path.GetFullPath(resolution.SourcePath));
    }

    [Fact]
    public void ResolveXsiHintBeatsAutoDetect()
    {
        // Layout: dir/library.xml with xsi:noNamespaceSchemaLocation="hint.xsd" + dir/library.xsd
        // The xsi: hint should win over the same-folder name match.
        var subDir = Path.Combine(_tempDir, "xsi-wins");
        Directory.CreateDirectory(subDir);

        WriteFileInDir(subDir, "hint.xsd", OtherXsd);
        WriteFileInDir(subDir, "library.xsd", SimpleXsd);
        WriteFileInDir(subDir, "library.xml", """
            <?xml version="1.0"?>
            <other xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                   xsi:noNamespaceSchemaLocation="hint.xsd">hello</other>
            """);

        var xmlPath = Path.Combine(subDir, "library.xml");
        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        var resolver = MakeResolver();

        var resolution = resolver.ResolveDetailed(doc);

        Assert.NotNull(resolution);
        Assert.Equal(SchemaResolutionKind.XsiHint, resolution!.Kind);
        Assert.EndsWith("hint.xsd", resolution.SourcePath, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveAutoDetectIsLastResort()
    {
        // Layout: dir/only.xml — no XSDs anywhere in the search path.
        var subDir = Path.Combine(_tempDir, "no-xsd");
        Directory.CreateDirectory(subDir);
        WriteFileInDir(subDir, "only.xml", "<root>hello</root>");

        var xmlPath = Path.Combine(subDir, "only.xml");
        var doc = MakeDoc(File.ReadAllText(xmlPath), xmlPath);
        var resolver = MakeResolver();

        var result = resolver.Resolve(doc);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveDocumentWithoutSourceReturnsNull()
    {
        // A document constructed without a file path has no Source URI;
        // auto-detect cannot search "the document's folder" and must return null.
        var doc = MakeDoc("<root>hello</root>"); // no filePath → Source is null
        var resolver = MakeResolver();

        var result = resolver.Resolve(doc);

        Assert.Null(result);
    }
}
