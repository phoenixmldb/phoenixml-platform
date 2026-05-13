using System.IO;
using System.Xml;
using System.Xml.Schema;
using Phoenixml.Platform.Editor.Xml.Validation;
using Xunit;

namespace Phoenixml.Platform.Editor.Xml.Tests.Validation;

public class SchemaDefaultValueResolverTests
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

    private static XmlSchemaType TypeOfElement(XmlSchemaSet set, string name)
    {
        foreach (XmlSchemaElement e in set.GlobalElements.Values)
            if (e.QualifiedName.Name == name) return e.ElementSchemaType!;
        throw new Xunit.Sdk.XunitException($"No element {name}");
    }

    [Fact]
    public void StringTypeReturnsEmpty()
    {
        var t = TypeOfElement(Load("""
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="e" type="xs:string"/>
            </xs:schema>
            """), "e");
        Assert.Equal("", SchemaDefaultValueResolver.GetDefault(t));
    }

    [Fact]
    public void IntegerTypeReturnsZero()
    {
        var t = TypeOfElement(Load("""
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="e" type="xs:integer"/>
            </xs:schema>
            """), "e");
        Assert.Equal("0", SchemaDefaultValueResolver.GetDefault(t));
    }

    [Fact]
    public void BooleanTypeReturnsFalse()
    {
        var t = TypeOfElement(Load("""
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="e" type="xs:boolean"/>
            </xs:schema>
            """), "e");
        Assert.Equal("false", SchemaDefaultValueResolver.GetDefault(t));
    }

    [Fact]
    public void DateTypeReturnsIsoToday()
    {
        var t = TypeOfElement(Load("""
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="e" type="xs:date"/>
            </xs:schema>
            """), "e");
        var result = SchemaDefaultValueResolver.GetDefault(t);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", result);
    }

    [Fact]
    public void EnumerationReturnsFirstValue()
    {
        var t = TypeOfElement(Load("""
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="e">
                <xs:simpleType>
                  <xs:restriction base="xs:string">
                    <xs:enumeration value="alpha"/>
                    <xs:enumeration value="beta"/>
                    <xs:enumeration value="gamma"/>
                  </xs:restriction>
                </xs:simpleType>
              </xs:element>
            </xs:schema>
            """), "e");
        Assert.Equal("alpha", SchemaDefaultValueResolver.GetDefault(t));
    }

    [Fact]
    public void NullTypeReturnsEmpty()
    {
        Assert.Equal("", SchemaDefaultValueResolver.GetDefault(null));
    }
}
