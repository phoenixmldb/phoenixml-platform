using System;
using System.IO;
using System.Threading.Tasks;
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
}
