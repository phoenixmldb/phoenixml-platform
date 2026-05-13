using System;
using System.Linq;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Clipboard;
using Xunit;
using PlatformDocumentId = Phoenixml.Platform.Abstractions.Documents.DocumentId;

namespace Phoenixml.Platform.Editor.Xml.Tests.Clipboard;

public class SubtreeSerializerTests
{
    // <library>
    //   <book isbn="978-0-13-468599-1">
    //     <title>The Pragmatic Programmer</title>
    //     <author>David Thomas</author>
    //   </book>
    //   <book isbn="978-0-201-63361-0">
    //     <title>Design Patterns</title>
    //   </book>
    // </library>
    private const string LibraryXml =
        "<library>" +
        "<book isbn=\"978-0-13-468599-1\">" +
        "<title>The Pragmatic Programmer</title>" +
        "<author>David Thomas</author>" +
        "</book>" +
        "<book isbn=\"978-0-201-63361-0\">" +
        "<title>Design Patterns</title>" +
        "</book>" +
        "</library>";

    private const string LeafXml = "<root><empty/></root>";
    private const string TextXml = "<root><item>hello</item></root>";

    private static (XmlDocument Doc, NodeId RootId) BuildDoc(string xml)
    {
        var (result, nsTable) = XmlDocument.ParseXml(xml);
        var doc = new XmlDocument(
            PlatformDocumentId.New(),
            source: null,
            "test.xml",
            result,
            nsTable);
        var rootId = result.Document.DocumentElement!.Value;
        return (doc, rootId);
    }

    private static NodeId FirstElementChild(XmlDocument doc, NodeId parentId)
    {
        var parent = (XdmElement)doc.Resolve(parentId)!;
        return parent.Children.First(id => doc.Resolve(id) is XdmElement);
    }

    [Fact]
    public void SerializeLeafElementReturnsSelfClosingOrInlineText()
    {
        var (doc, rootId) = BuildDoc(LeafXml);
        var emptyId = FirstElementChild(doc, rootId);

        var xml = SubtreeSerializer.Serialize(doc, emptyId);

        // Must be well-formed XML: parseable and preserves local name.
        var (result, _) = XmlDocument.ParseXml(xml);
        var el = doc.Resolve(emptyId) as XdmElement;
        Assert.NotNull(el);
        Assert.Contains(el.LocalName, xml, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeElementWithAttributesPreservesThem()
    {
        var (doc, rootId) = BuildDoc(LibraryXml);
        var bookId = FirstElementChild(doc, rootId);
        var book = (XdmElement)doc.Resolve(bookId)!;

        var xml = SubtreeSerializer.Serialize(doc, bookId);

        // isbn attribute must be present.
        Assert.Contains("isbn", xml, StringComparison.Ordinal);
        Assert.Contains("978-0-13-468599-1", xml, StringComparison.Ordinal);
        // Title child must be present.
        Assert.Contains("<title>", xml, StringComparison.Ordinal);
        Assert.Contains("Pragmatic Programmer", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeElementWithDescendants()
    {
        var (doc, rootId) = BuildDoc(LibraryXml);
        var bookId = FirstElementChild(doc, rootId);

        var xml = SubtreeSerializer.Serialize(doc, bookId);

        // Full subtree: book > title, author.
        Assert.Contains("<title>", xml, StringComparison.Ordinal);
        Assert.Contains("<author>", xml, StringComparison.Ordinal);
        Assert.Contains("David Thomas", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeOmitsXmlDeclaration()
    {
        var (doc, rootId) = BuildDoc(LibraryXml);
        var bookId = FirstElementChild(doc, rootId);

        var xml = SubtreeSerializer.Serialize(doc, bookId);

        Assert.False(xml.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase),
            "Output should not begin with an XML declaration.");
    }

    [Fact]
    public void SerializeUtf8Encoding()
    {
        var (doc, rootId) = BuildDoc(TextXml);
        var itemId = FirstElementChild(doc, rootId);

        var xml = SubtreeSerializer.Serialize(doc, itemId);

        // Result is a .NET string (UTF-16 in memory) with no BOM prefix.
        Assert.False(xml.StartsWith('﻿'), "Output must not start with a BOM.");
        Assert.NotEmpty(xml);
    }

    [Fact]
    public void SerializeAttributeOrderMatchesSource()
    {
        // Build a doc with an element that has multiple attributes.
        const string multiAttrXml = "<root><item a=\"1\" b=\"2\" c=\"3\"/></root>";
        var (doc, rootId) = BuildDoc(multiAttrXml);
        var itemId = FirstElementChild(doc, rootId);

        var xml = SubtreeSerializer.Serialize(doc, itemId);

        // All three attributes must appear; order from source is preserved.
        var posA = xml.IndexOf("a=", StringComparison.Ordinal);
        var posB = xml.IndexOf("b=", StringComparison.Ordinal);
        var posC = xml.IndexOf("c=", StringComparison.Ordinal);
        Assert.True(posA < posB && posB < posC,
            "Attribute order must match source declaration order.");
    }

    [Fact]
    public void SerializeNonElementNodeThrows()
    {
        // Build a doc with an attribute node we can grab by id.
        const string attrXml = "<root id=\"r1\"/>";
        var (result, nsTable) = XmlDocument.ParseXml(attrXml);
        var doc = new XmlDocument(PlatformDocumentId.New(), source: null, "test.xml", result, nsTable);

        var rootElem = (XdmElement)doc.Resolve(result.Document.DocumentElement!.Value)!;
        // The first item in Attributes is an XdmAttribute.
        var attrId = rootElem.Attributes[0];

        var ex = Assert.Throws<ArgumentException>(() => SubtreeSerializer.Serialize(doc, attrId));
        Assert.Equal("rootId", ex.ParamName);
    }

    [Fact]
    public void SerializeMissingNodeIdThrows()
    {
        var (doc, _) = BuildDoc(LibraryXml);
        var missingId = new NodeId(ulong.MaxValue - 1);

        Assert.Throws<InvalidOperationException>(() => SubtreeSerializer.Serialize(doc, missingId));
    }

    [Fact]
    public void SerializeNullDocThrows()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SubtreeSerializer.Serialize(null!, new NodeId(1)));
    }

    [Fact]
    public void SerializeWithAncestorNamespaceDeclsEmitsThemOnRoot()
    {
        // The <item> element uses a prefix declared on its ancestor <root>.
        // After serialization, the output must be self-contained parseable XML.
        const string nsXml = "<root xmlns:bk=\"http://example.com/book\"><bk:item bk:attr=\"v\"/></root>";
        var (doc, rootId) = BuildDoc(nsXml);
        var itemId = FirstElementChild(doc, rootId);

        var xml = SubtreeSerializer.Serialize(doc, itemId);

        // The serialized fragment must be parseable on its own.
        var ex = Record.Exception(() => XmlDocument.ParseXml(xml));
        Assert.Null(ex);

        // The bk namespace must appear in the output.
        Assert.Contains("http://example.com/book", xml, StringComparison.Ordinal);
        // Attribute value preserved.
        Assert.Contains("bk:attr", xml, StringComparison.Ordinal);
    }
}
