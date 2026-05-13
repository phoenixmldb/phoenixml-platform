using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Schema;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Validation;
using Xunit;
using PlatformDocumentId = Phoenixml.Platform.Abstractions.Documents.DocumentId;

namespace Phoenixml.Platform.Editor.Xml.Tests.Validation;

public class SchemaElementResolverTests
{
    // -------------------------------------------------------------------------
    // Shared library fixture.
    //
    //   <library>
    //     <book isbn="...">
    //       <title>...</title>
    //       <author>...</author>
    //       <year>...</year>
    //     </book>
    //   </library>
    // -------------------------------------------------------------------------

    private const string LibraryXsd = """
        <?xml version="1.0"?>
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="library">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="book" maxOccurs="unbounded">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="title"  type="xs:string"/>
                      <xs:element name="author" type="xs:string"/>
                      <xs:element name="year"   type="xs:gYear"/>
                    </xs:sequence>
                    <xs:attribute name="isbn" type="xs:string" use="required"/>
                  </xs:complexType>
                </xs:element>
              </xs:sequence>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    private const string LibraryXml = """
        <library>
          <book isbn="978-0-06-112008-4">
            <title>To Kill a Mockingbird</title>
            <author>Harper Lee</author>
            <year>1960</year>
          </book>
        </library>
        """;

    // -------------------------------------------------------------------------
    // Helpers.
    // -------------------------------------------------------------------------

    private static XmlSchemaSet LoadSchemaFromString(string xsd)
    {
        var schemas = new XmlSchemaSet();
        using var reader = XmlReader.Create(new StringReader(xsd));
        schemas.Add(targetNamespace: null, reader);
        schemas.Compile();
        return schemas;
    }

    private static XmlDocument BuildDoc(string xml)
    {
        var (result, nsTable) = XmlDocument.ParseXml(xml);
        return new XmlDocument(
            PlatformDocumentId.New(),
            source: null,
            displayName: "test.xml",
            parseResult: result,
            namespaceById: nsTable);
    }

    /// <summary>
    /// Finds the first element with the given local name by BFS over the document tree.
    /// </summary>
    private static XdmElement FindElement(XmlDocument doc, string localName)
    {
        var queue = new Queue<NodeId>(doc.Root.Children);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (doc.Resolve(id) is not XdmElement el) continue;
            if (el.LocalName.Equals(localName, StringComparison.Ordinal)) return el;
            foreach (var child in el.Children) queue.Enqueue(child);
        }
        throw new InvalidOperationException($"Element <{localName}> not found in document.");
    }

    // -------------------------------------------------------------------------
    // Tests.
    // -------------------------------------------------------------------------

    [Fact]
    public void ResolveDocumentRootReturnsRootDeclaration()
    {
        var schemas = LoadSchemaFromString(LibraryXsd);
        var doc = BuildDoc(LibraryXml);

        var library = FindElement(doc, "library");
        var result = SchemaElementResolver.Resolve(doc, library.Id, schemas);

        Assert.NotNull(result);
        Assert.Equal("library", result!.Name);
    }

    [Fact]
    public void ResolveNestedElementInSequence()
    {
        var schemas = LoadSchemaFromString(LibraryXsd);
        var doc = BuildDoc(LibraryXml);

        var book = FindElement(doc, "book");
        var result = SchemaElementResolver.Resolve(doc, book.Id, schemas);

        Assert.NotNull(result);
        Assert.Equal("book", result!.Name);
    }

    [Fact]
    public void ResolveDeeplyNestedElement()
    {
        var schemas = LoadSchemaFromString(LibraryXsd);
        var doc = BuildDoc(LibraryXml);

        var title = FindElement(doc, "title");
        var result = SchemaElementResolver.Resolve(doc, title.Id, schemas);

        Assert.NotNull(result);
        Assert.Equal("title", result!.Name);
    }

    [Fact]
    public void ResolveElementNotDeclaredReturnsNull()
    {
        // Build a doc with an extra element that is not in the schema.
        const string xml = "<library><unknown/></library>";
        var schemas = LoadSchemaFromString(LibraryXsd);
        var doc = BuildDoc(xml);

        var unknown = FindElement(doc, "unknown");
        var result = SchemaElementResolver.Resolve(doc, unknown.Id, schemas);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveWhenNoSchemaSetReturnsNull()
    {
        var emptySchemas = new XmlSchemaSet();
        emptySchemas.Compile();
        var doc = BuildDoc(LibraryXml);

        var library = FindElement(doc, "library");
        var result = SchemaElementResolver.Resolve(doc, library.Id, emptySchemas);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveSequenceAndChoiceTraversal()
    {
        // XSD with an xs:choice nested inside an xs:sequence.
        // The target element is only reachable by traversing the choice branch.
        const string xsd = """
            <?xml version="1.0"?>
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="root">
                <xs:complexType>
                  <xs:sequence>
                    <xs:element name="header" type="xs:string"/>
                    <xs:choice>
                      <xs:element name="optionA" type="xs:string"/>
                      <xs:element name="optionB" type="xs:string"/>
                    </xs:choice>
                  </xs:sequence>
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """;

        const string xml = "<root><header>h</header><optionB>b</optionB></root>";

        var schemas = LoadSchemaFromString(xsd);
        var doc = BuildDoc(xml);

        var optionB = FindElement(doc, "optionB");
        var result = SchemaElementResolver.Resolve(doc, optionB.Id, schemas);

        Assert.NotNull(result);
        Assert.Equal("optionB", result!.Name);
    }
}
