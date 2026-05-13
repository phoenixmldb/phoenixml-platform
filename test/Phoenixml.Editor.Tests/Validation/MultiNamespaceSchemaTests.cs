using System;
using System.IO;
using Phoenixml.Editor.Settings;
using Phoenixml.Editor.Validation;
using Phoenixml.Platform.Editor.Xml;
using Xunit;

namespace Phoenixml.Editor.Tests.Validation;

public class MultiNamespaceSchemaTests
{
    private static XmlDocument ParseDoc(string xml, string filePath)
    {
        File.WriteAllText(filePath, xml);
        return XmlDocument.LoadAsync(filePath, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    private const string XsdA = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   targetNamespace="urn:a" xmlns="urn:a"
                   elementFormDefault="qualified">
          <xs:element name="root">
            <xs:complexType>
              <xs:sequence>
                <xs:any namespace="##any" processContents="lax"/>
              </xs:sequence>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    private const string XsdB = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   targetNamespace="urn:b" xmlns="urn:b"
                   elementFormDefault="qualified">
          <xs:element name="thing" type="xs:string"/>
        </xs:schema>
        """;

    [Fact]
    public void TwoNamespaceDocResolvesBothXsds()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"phoenixml-mns-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var a = Path.Combine(dir, "a.xsd"); File.WriteAllText(a, XsdA);
            var b = Path.Combine(dir, "b.xsd"); File.WriteAllText(b, XsdB);
            var xml = $"""
                <a:root xmlns:a="urn:a" xmlns:b="urn:b"
                        xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                        xsi:schemaLocation="urn:a a.xsd urn:b b.xsd">
                  <b:thing>hello</b:thing>
                </a:root>
                """;
            var doc = ParseDoc(xml, Path.Combine(dir, "doc.xml"));
            var resolver = new SchemaResolver(new EditorSettingsStore());
            var schemas = resolver.Resolve(doc);

            Assert.NotNull(schemas);
            // Combined set carries elements from both namespaces.
            var hasRoot = false; var hasThing = false;
            foreach (System.Xml.Schema.XmlSchemaElement e in schemas!.GlobalElements.Values)
            {
                if (e.QualifiedName.Namespace == "urn:a" && e.QualifiedName.Name == "root") hasRoot = true;
                if (e.QualifiedName.Namespace == "urn:b" && e.QualifiedName.Name == "thing") hasThing = true;
            }
            Assert.True(hasRoot, "urn:a:root missing from combined set");
            Assert.True(hasThing, "urn:b:thing missing from combined set");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MalformedPairsReturnNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"phoenixml-mns-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // Odd number of tokens in schemaLocation
            var xml = """
                <a:root xmlns:a="urn:a"
                        xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                        xsi:schemaLocation="urn:a"/>
                """;
            var doc = ParseDoc(xml, Path.Combine(dir, "doc.xml"));
            var schemas = new SchemaResolver(new EditorSettingsStore()).Resolve(doc);
            Assert.Null(schemas);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MissingFileReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"phoenixml-mns-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var xml = """
                <a:root xmlns:a="urn:a"
                        xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                        xsi:schemaLocation="urn:a missing.xsd"/>
                """;
            var doc = ParseDoc(xml, Path.Combine(dir, "doc.xml"));
            var schemas = new SchemaResolver(new EditorSettingsStore()).Resolve(doc);
            Assert.Null(schemas);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
