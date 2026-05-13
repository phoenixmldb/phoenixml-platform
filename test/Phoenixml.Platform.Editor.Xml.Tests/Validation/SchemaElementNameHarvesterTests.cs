using System.IO;
using System.Xml;
using System.Xml.Schema;
using Phoenixml.Platform.Editor.Xml.Validation;
using Xunit;

namespace Phoenixml.Platform.Editor.Xml.Tests.Validation;

public class SchemaElementNameHarvesterTests
{
    private static XmlSchemaSet LoadFromString(string xsd)
    {
        var set = new XmlSchemaSet();
        using var sr = new StringReader(xsd);
        using var xr = XmlReader.Create(sr, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        set.Add(XmlSchema.Read(xr, validationEventHandler: null!)!);
        set.Compile();
        return set;
    }

    private const string LibrarySchema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="library">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="book" maxOccurs="unbounded">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="title" type="xs:string"/>
                      <xs:element name="author" type="xs:string"/>
                    </xs:sequence>
                    <xs:attribute name="isbn" type="xs:string"/>
                  </xs:complexType>
                </xs:element>
              </xs:sequence>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    [Fact]
    public void HarvestsRootGlobalElement()
    {
        var set = LoadFromString(LibrarySchema);
        var names = SchemaElementNameHarvester.AllReachableElementNames(set);
        Assert.Contains("library", names);
    }

    [Fact]
    public void HarvestsNestedElements()
    {
        var set = LoadFromString(LibrarySchema);
        var names = SchemaElementNameHarvester.AllReachableElementNames(set);
        Assert.Contains("book", names);
        Assert.Contains("title", names);
        Assert.Contains("author", names);
    }

    [Fact]
    public void HarvestsAttributeNames()
    {
        var set = LoadFromString(LibrarySchema);
        var attrs = SchemaElementNameHarvester.AllAttributeNames(set);
        Assert.Contains("isbn", attrs);
    }

    [Fact]
    public void ResultIsSortedAndDistinct()
    {
        var set = LoadFromString(LibrarySchema);
        var names = SchemaElementNameHarvester.AllReachableElementNames(set);
        for (int i = 1; i < names.Count; i++)
            Assert.True(string.CompareOrdinal(names[i - 1], names[i]) < 0, $"order: {names[i - 1]} >= {names[i]}");
    }
}
