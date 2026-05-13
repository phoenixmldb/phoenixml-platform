using System.IO;
using System.Xml;
using System.Xml.Schema;
using Phoenixml.Platform.Editor.Xml.Validation;
using Xunit;

namespace Phoenixml.Platform.Editor.Xml.Tests.Validation;

public class SchemaAnnotationLookupTests
{
    private static XmlSchemaSet Load(string xsd)
    {
        var set = new XmlSchemaSet();
        using var sr = new StringReader(xsd);
        using var xr = XmlReader.Create(sr, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        set.Add(XmlSchema.Read(xr, validationEventHandler: null!)!);
        set.Compile();
        return set;
    }

    private static XmlSchemaElement FindElement(XmlSchemaSet set, string name)
    {
        foreach (XmlSchemaElement e in set.GlobalElements.Values)
            if (e.QualifiedName.Name == name) return e;
        throw new Xunit.Sdk.XunitException($"No global element {name}");
    }

    [Fact]
    public void ReturnsDocumentationForAnnotatedElement()
    {
        var xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="book">
                <xs:annotation>
                  <xs:documentation>A book in the library.</xs:documentation>
                </xs:annotation>
              </xs:element>
            </xs:schema>
            """;
        var element = FindElement(Load(xsd), "book");
        Assert.Equal("A book in the library.", SchemaAnnotationLookup.GetDocumentation(element));
    }

    [Fact]
    public void ReturnsNullWhenNoAnnotation()
    {
        var xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="book" type="xs:string"/>
            </xs:schema>
            """;
        var element = FindElement(Load(xsd), "book");
        Assert.Null(SchemaAnnotationLookup.GetDocumentation(element));
    }

    [Fact]
    public void ConcatenatesMultipleDocumentationNodes()
    {
        var xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="book">
                <xs:annotation>
                  <xs:documentation>Part one.</xs:documentation>
                  <xs:documentation>Part two.</xs:documentation>
                </xs:annotation>
              </xs:element>
            </xs:schema>
            """;
        var element = FindElement(Load(xsd), "book");
        Assert.Equal("Part one. Part two.", SchemaAnnotationLookup.GetDocumentation(element));
    }
}
