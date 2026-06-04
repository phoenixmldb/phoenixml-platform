using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Phoenixml.Workbench.Settings;
using Phoenixml.Editor.Validation;
using Phoenixml.Platform.Abstractions.Editing;
using Phoenixml.Platform.Editor.Xml;
using Xunit;
using PlatformDocumentId = Phoenixml.Platform.Abstractions.Documents.DocumentId;

namespace Phoenixml.Editor.Tests.Validation;

/// <summary>
/// Tests for <see cref="EditorValidationService"/>.
/// </summary>
/// <remarks>
/// Tests trigger validation via <see cref="EditorValidationService.SetActiveDocument"/> and
/// then wait 700ms (100ms beyond the 500ms debounce window) for the result to appear in
/// <see cref="EditorValidationService.Current"/>.
/// </remarks>
public sealed class EditorValidationServiceTests : IDisposable
{
    private readonly string _tempDir;

    // A minimal XSD: allows <root> as a string element.
    private const string SimpleXsd = """
        <?xml version="1.0"?>
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="root" type="xs:string"/>
        </xs:schema>
        """;

    public EditorValidationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"phoenixml-vsvc-test-{Guid.NewGuid()}");
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

    private SchemaResolver MakeResolver(Dictionary<string, string>? perDocSchema = null)
    {
        var settingsPath = Path.Combine(_tempDir, $"settings-{Guid.NewGuid()}.json");
        var store = new EditorSettingsStore(settingsPath);

        if (perDocSchema is not null)
        {
            foreach (var kvp in perDocSchema)
                store.Settings.PerDocumentSchema.Add(kvp.Key, kvp.Value);
        }

        return new SchemaResolver(store);
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DiagnosticsEmptyWhenNoSchema()
    {
        // A document with no xsi: hints and no settings entry → no schema → no diagnostics.
        var doc = MakeDoc("<root>hello</root>");
        var resolver = MakeResolver();
        using var service = new EditorValidationService(resolver);

        IReadOnlyList<IDiagnostic>? fired = null;
        service.DiagnosticsChanged += d => fired = d;

        service.SetActiveDocument(doc);

        // Wait beyond the 500ms debounce window.
        await Task.Delay(700, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Empty(service.Current);
        // When no schema is found the service publishes an empty list — verify it fired.
        Assert.NotNull(fired);
        Assert.Empty(fired!);
    }

    [Fact]
    public async Task DiagnosticsContainErrorOnInvalidDocument()
    {
        // XSD that allows only <root> as a string element (no child elements).
        var xsdPath = WriteFile("schema.xsd", SimpleXsd);
        // XML that violates the schema: has an unexpected child element.
        var xmlContent = await File.ReadAllTextAsync(
            WriteFile("invalid.xml", $"""
                <?xml version="1.0"?>
                <root xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                      xsi:noNamespaceSchemaLocation="schema.xsd">
                  <unexpected/>
                </root>
                """),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        var xmlPath = Path.Combine(_tempDir, "invalid.xml");
        var doc = MakeDoc(xmlContent, xmlPath);
        var resolver = MakeResolver();
        using var service = new EditorValidationService(resolver);

        IReadOnlyList<IDiagnostic>? fired = null;
        service.DiagnosticsChanged += d => fired = d;

        service.SetActiveDocument(doc);
        await Task.Delay(700, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.NotEmpty(service.Current);
        Assert.Contains(service.Current, d => d.Severity == DiagnosticSeverity.Error);
        Assert.NotNull(fired);
        _ = xsdPath;
    }

    [Fact]
    public async Task SetActiveDocumentToNullClearsDiagnostics()
    {
        // Start with a doc that produces diagnostics, then null it out.
        var xsdPath = WriteFile("schema2.xsd", SimpleXsd);
        var xmlContent = await File.ReadAllTextAsync(
            WriteFile("invalid2.xml", $"""
                <?xml version="1.0"?>
                <root xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                      xsi:noNamespaceSchemaLocation="schema2.xsd">
                  <bad/>
                </root>
                """),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        var xmlPath = Path.Combine(_tempDir, "invalid2.xml");
        var doc = MakeDoc(xmlContent, xmlPath);
        var resolver = MakeResolver();
        using var service = new EditorValidationService(resolver);

        service.SetActiveDocument(doc);
        await Task.Delay(700, TestContext.Current.CancellationToken).ConfigureAwait(true);

        // Should have errors now.
        Assert.NotEmpty(service.Current);

        // Null out the active doc.
        service.SetActiveDocument(null);
        await Task.Delay(700, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Empty(service.Current);
        _ = xsdPath;
    }
}
