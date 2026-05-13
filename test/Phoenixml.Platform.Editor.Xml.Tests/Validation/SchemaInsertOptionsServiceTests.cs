using System;
using System.IO;
using System.Xml;
using System.Xml.Schema;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Editing;
using Phoenixml.Platform.Editor.Xml.Validation;
using Xunit;
using PlatformDocumentId = Phoenixml.Platform.Abstractions.Documents.DocumentId;

namespace Phoenixml.Platform.Editor.Xml.Tests.Validation;

public class SchemaInsertOptionsServiceTests
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

    // A library with one book that does NOT have an isbn attribute yet.
    private const string LibraryXmlNoIsbn = """
        <library>
          <book>
            <title>To Kill a Mockingbird</title>
            <author>Harper Lee</author>
            <year>1960</year>
          </book>
        </library>
        """;

    // A library with one book that already has an isbn attribute.
    private const string LibraryXmlWithIsbn = """
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
    public void ResolveBookReturnsTitleAuthorYear()
    {
        var schemas = LoadSchemaFromString(LibraryXsd);
        var doc = BuildDoc(LibraryXmlNoIsbn);

        var book = FindElement(doc, "book");
        var options = SchemaInsertOptionsService.Resolve(doc, book.Id, schemas);

        Assert.Equal(["title", "author", "year"], options.ChildElementNames);
    }

    [Fact]
    public void ResolveBookReturnsIsbnAttribute()
    {
        var schemas = LoadSchemaFromString(LibraryXsd);
        var doc = BuildDoc(LibraryXmlNoIsbn);

        var book = FindElement(doc, "book");
        var options = SchemaInsertOptionsService.Resolve(doc, book.Id, schemas);

        Assert.Contains("isbn", options.AttributeNames, StringComparer.Ordinal);
    }

    [Fact]
    public void ResolveBookWithIsbnPresentExcludesIt()
    {
        var schemas = LoadSchemaFromString(LibraryXsd);
        var doc = BuildDoc(LibraryXmlWithIsbn);

        var book = FindElement(doc, "book");
        var options = SchemaInsertOptionsService.Resolve(doc, book.Id, schemas);

        // isbn is already present on the element; it must be excluded from the attribute list.
        Assert.Empty(options.AttributeNames);
    }

    [Fact]
    public void ResolveLibraryReturnsBook()
    {
        var schemas = LoadSchemaFromString(LibraryXsd);
        var doc = BuildDoc(LibraryXmlNoIsbn);

        var library = FindElement(doc, "library");
        var options = SchemaInsertOptionsService.Resolve(doc, library.Id, schemas);

        Assert.Contains("book", options.ChildElementNames, StringComparer.Ordinal);
    }

    [Fact]
    public void ResolveTitleReturnsEmpty()
    {
        // <title> is xs:string (simple type) — no child elements or attributes declared.
        var schemas = LoadSchemaFromString(LibraryXsd);
        var doc = BuildDoc(LibraryXmlNoIsbn);

        var title = FindElement(doc, "title");
        var options = SchemaInsertOptionsService.Resolve(doc, title.Id, schemas);

        Assert.Empty(options.ChildElementNames);
        Assert.Empty(options.AttributeNames);
    }

    [Fact]
    public void ResolveNoSchemaReturnsEmpty()
    {
        var emptySchemas = new XmlSchemaSet();
        emptySchemas.Compile();
        var doc = BuildDoc(LibraryXmlNoIsbn);

        var book = FindElement(doc, "book");
        var options = SchemaInsertOptionsService.Resolve(doc, book.Id, emptySchemas);

        Assert.Empty(options.ChildElementNames);
        Assert.Empty(options.AttributeNames);
    }

    [Fact]
    public void ResolveElementNotDeclaredReturnsEmpty()
    {
        // The root element <unknown> is not in the schema — Resolve should return empty.
        const string xml = "<unknown><child/></unknown>";
        var schemas = LoadSchemaFromString(LibraryXsd);
        var doc = BuildDoc(xml);

        var child = FindElement(doc, "child");
        var options = SchemaInsertOptionsService.Resolve(doc, child.Id, schemas);

        Assert.Empty(options.ChildElementNames);
        Assert.Empty(options.AttributeNames);
    }
}
