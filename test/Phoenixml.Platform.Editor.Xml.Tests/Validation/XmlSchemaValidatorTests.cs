using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Abstractions.Editing;
using Phoenixml.Platform.Editor.Xml;
using Xunit;
using SchemaValidator = Phoenixml.Platform.Editor.Xml.Validation.XmlSchemaValidator;

namespace Phoenixml.Platform.Editor.Xml.Tests.Validation;

public class XmlSchemaValidatorTests
{
    // A minimal XSD: allows <order> with a required <id> string child.
    private const string OrderXsd = """
        <?xml version="1.0"?>
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="order">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="id" type="xs:string"/>
              </xs:sequence>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    // Valid XML matching the schema
    private const string ValidXml = "<order><id>ORD-001</id></order>";

    // Invalid XML: contains an extra element <extra> not allowed by the schema
    private const string InvalidXml = "<order><id>ORD-001</id><extra>nope</extra></order>";

    private static async Task<(string XsdPath, string XmlPath)> WriteFixtures(
        string dir, string xml, System.Threading.CancellationToken ct)
    {
        var xsdPath = Path.Combine(dir, "order.xsd");
        var xmlPath = Path.Combine(dir, "order.xml");
        await File.WriteAllTextAsync(xsdPath, OrderXsd, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(xmlPath, xml, ct).ConfigureAwait(false);
        return (xsdPath, xmlPath);
    }

    [Fact]
    public async Task ValidateValidDocumentReturnsEmptyDiagnostics()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var (xsdPath, xmlPath) = await WriteFixtures(dir, ValidXml, TestContext.Current.CancellationToken).ConfigureAwait(true);
            var schemas = SchemaValidator.LoadSchema(xsdPath);
            var doc = await XmlDocument.LoadAsync(xmlPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

            var diagnostics = SchemaValidator.Validate(doc, schemas);

            Assert.Empty(diagnostics);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateInvalidDocumentReportsErrors()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var (xsdPath, xmlPath) = await WriteFixtures(dir, InvalidXml, TestContext.Current.CancellationToken).ConfigureAwait(true);
            var schemas = SchemaValidator.LoadSchema(xsdPath);
            var doc = await XmlDocument.LoadAsync(xmlPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

            var diagnostics = SchemaValidator.Validate(doc, schemas);

            Assert.NotEmpty(diagnostics);
            Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateReportsConstraintViolationsWithCorrectSeverity()
    {
        // Element in wrong order violates sequence constraint
        const string xml = "<order><extra/><id>X</id></order>";
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var (xsdPath, xmlPath) = await WriteFixtures(dir, xml, TestContext.Current.CancellationToken).ConfigureAwait(true);
            var schemas = SchemaValidator.LoadSchema(xsdPath);
            var doc = await XmlDocument.LoadAsync(xmlPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

            var diagnostics = SchemaValidator.Validate(doc, schemas);

            Assert.NotEmpty(diagnostics);
            Assert.All(diagnostics, d =>
            {
                Assert.True(
                    d.Severity == DiagnosticSeverity.Error || d.Severity == DiagnosticSeverity.Warning,
                    $"Unexpected severity: {d.Severity}");
            });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadSchemaNonExistentFileThrows()
    {
        // XmlSchemaSet.Add throws an IOException when the file doesn't exist
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xsd");
        Assert.Throws<FileNotFoundException>(() => SchemaValidator.LoadSchema(path));
    }

    [Fact]
    public async Task ValidateInvalidDocPopulatesLocationNodeId()
    {
        // The push-based validator attaches each diagnostic to the EXACT XdmNode that
        // was active when the validation event fired, so NodeId resolution must succeed
        // and the source position must be the node's own parse-time line/column.
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var (xsdPath, xmlPath) = await WriteFixtures(
                dir, InvalidXml, TestContext.Current.CancellationToken).ConfigureAwait(true);
            var schemas = SchemaValidator.LoadSchema(xsdPath);
            var doc = await XmlDocument.LoadAsync(
                xmlPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

            var diagnostics = SchemaValidator.Validate(doc, schemas);

            // At least one diagnostic must carry a mapped Location with a valid NodeId.
            var withLocation = diagnostics
                .Where(d => d.Location is not null)
                .ToList();

            Assert.NotEmpty(withLocation);

            foreach (var d in withLocation)
            {
                var loc = d.Location!;
                // NodeId must resolve to a real node in the document.
                var node = doc.Resolve(loc.NodeId!.Value);
                Assert.NotNull(node);
                // The resolved node must be an element (push approach attaches to elements).
                Assert.True(node is XdmElement, $"Expected XdmElement but got {node.GetType().Name}");
                // Line and column must be positive.
                Assert.True(loc.Line > 0, $"Expected Line > 0, got {loc.Line}");
                Assert.True(loc.Column > 0, $"Expected Column > 0, got {loc.Column}");
                // The Location line/col must match the node's own parse-time position —
                // not a serialized-output position.
                Assert.Equal(node.SourceLine, loc.Line);
                Assert.Equal(node.SourceColumn, loc.Column);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // -------------------------------------------------------------------------
    // Library fixture for push-based correctness test.
    // -------------------------------------------------------------------------

    // XSD: a library containing one or more books, each with a required isbn attribute
    // and a required <year> element typed as xs:gYear.
    private const string LibraryXsd = """
        <?xml version="1.0"?>
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="library">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="book" maxOccurs="unbounded">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="year" type="xs:gYear"/>
                    </xs:sequence>
                    <xs:attribute name="isbn" type="xs:string" use="required"/>
                  </xs:complexType>
                </xs:element>
              </xs:sequence>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    // Two books: the first is valid; the second is missing the isbn attribute and has
    // an invalid gYear value. Each is on its own line so source positions differ.
    private const string LibraryXml = """
        <library>
          <book isbn="978-0-06-112008-4"><year>1960</year></book>
          <book><year>not-a-year</year></book>
        </library>
        """;

    /// <summary>
    /// Verifies that the push-based walk attaches diagnostics to the correct XdmNodes.
    /// The second &lt;book&gt; is missing its isbn attribute; the second &lt;year&gt;
    /// has an invalid gYear value. Both diagnostics must resolve to nodes on lines that
    /// differ from the first (valid) book.
    /// </summary>
    [Fact]
    public async Task ValidatePushBasedAttachesDiagnosticsToCorrectNode()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var xsdPath = Path.Combine(dir, "library.xsd");
            var xmlPath = Path.Combine(dir, "library.xml");
            await File.WriteAllTextAsync(xsdPath, LibraryXsd, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await File.WriteAllTextAsync(xmlPath, LibraryXml, TestContext.Current.CancellationToken).ConfigureAwait(true);

            var schemas = SchemaValidator.LoadSchema(xsdPath);
            var doc = await XmlDocument.LoadAsync(xmlPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

            var diagnostics = SchemaValidator.Validate(doc, schemas);

            // Expect at least two errors: missing isbn + invalid gYear.
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count >= 2, $"Expected at least 2 errors, got {errors.Count}: {string.Join("; ", errors.Select(e => e.Message))}");

            // All error diagnostics must carry a Location that resolves to a real node.
            foreach (var d in errors)
            {
                Assert.NotNull(d.Location);
                var node = doc.Resolve(d.Location!.NodeId!.Value);
                Assert.NotNull(node);
            }

            // ---- isbn-missing error attaches to the second <book> element ----
            // The second book is on line 3 (1-based) in the library XML.
            var isbnError = errors.FirstOrDefault(d =>
                d.Message.Contains("isbn", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(isbnError);
            var isbnNode = doc.Resolve(isbnError!.Location!.NodeId!.Value) as XdmElement;
            Assert.NotNull(isbnNode);
            Assert.Equal("book", isbnNode!.LocalName);
            // The second <book> must be on a DIFFERENT line from the first <book>.
            // (First <book> is on line 2; second is on line 3.)
            Assert.True(isbnNode.SourceLine > 1,
                $"Expected second <book> on line > 1, got {isbnNode.SourceLine}");

            // ---- gYear error attaches to the <year> element of the second book ----
            var yearError = errors.FirstOrDefault(d =>
                d.Message.Contains("gYear", StringComparison.OrdinalIgnoreCase)
                || d.Message.Contains("not-a-year", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(yearError);
            var yearNode = doc.Resolve(yearError!.Location!.NodeId!.Value) as XdmElement;
            Assert.NotNull(yearNode);
            Assert.Equal("year", yearNode!.LocalName);
            // The invalid <year> must be on the SAME line as the second <book> or later,
            // but not on the same line as the first valid <year> (line 2).
            Assert.True(yearNode.SourceLine > 2,
                $"Expected invalid <year> on line > 2, got {yearNode.SourceLine}");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
