using System;
using System.Linq;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Find;
using Xunit;

namespace Phoenixml.Platform.Editor.Xml.Tests.Find;

/// <summary>
/// Unit tests for <see cref="FindService"/>. Uses inline XML parsed via
/// <see cref="XmlDocument.ParseXml"/> to avoid file I/O.
/// </summary>
public class FindServiceTests
{
    // Shared fixture: a small library document with elements, attributes, and text.
    private const string LibraryXml = """
        <library>
          <book isbn="978-0-13-110362-7">
            <title>The C Programming Language</title>
            <author>Kernighan</author>
          </book>
          <book isbn="978-0-13-468599-1">
            <title>Effective Java</title>
            <author>Bloch</author>
          </book>
        </library>
        """;

    private static XmlDocument ParseDoc(string xml)
    {
        var (result, nsTable) = XmlDocument.ParseXml(xml);
        return new XmlDocument(
            Phoenixml.Platform.Abstractions.Documents.DocumentId.New(),
            source: null,
            displayName: "test.xml",
            result,
            nsTable);
    }

    [Fact]
    public void FindByElementLocalName()
    {
        var doc = ParseDoc(LibraryXml);

        var matches = FindService.Find(doc, "book", matchCase: false);

        // Two <book> elements.
        Assert.Equal(2, matches.Count);
        Assert.All(matches, id =>
        {
            var node = doc.Resolve(id);
            Assert.IsType<XdmElement>(node);
            Assert.Equal("book", ((XdmElement)node).LocalName);
        });
    }

    [Fact]
    public void FindByAttributeLocalName()
    {
        var doc = ParseDoc(LibraryXml);

        var matches = FindService.Find(doc, "isbn", matchCase: false);

        // Two @isbn attributes.
        Assert.Equal(2, matches.Count);
        Assert.All(matches, id =>
        {
            var node = doc.Resolve(id);
            Assert.IsType<XdmAttribute>(node);
            Assert.Equal("isbn", ((XdmAttribute)node).LocalName);
        });
    }

    [Fact]
    public void FindByAttributeValue()
    {
        var doc = ParseDoc(LibraryXml);

        // A substring of the first ISBN value.
        var matches = FindService.Find(doc, "978-0-13-110362", matchCase: false);

        Assert.Single(matches);
        var node = doc.Resolve(matches[0]);
        Assert.IsType<XdmAttribute>(node);
        Assert.Equal("isbn", ((XdmAttribute)node).LocalName);
    }

    [Fact]
    public void FindByElementText()
    {
        var doc = ParseDoc(LibraryXml);

        var matches = FindService.Find(doc, "Bloch", matchCase: false);

        Assert.Single(matches);
        var node = doc.Resolve(matches[0]);
        Assert.IsType<XdmText>(node);
        Assert.Contains("Bloch", ((XdmText)node).Value, StringComparison.Ordinal);
    }

    [Fact]
    public void FindCaseInsensitiveByDefault()
    {
        var doc = ParseDoc(LibraryXml);

        // "BOOK" in uppercase — should still match <book> with matchCase=false.
        var matches = FindService.Find(doc, "BOOK", matchCase: false);

        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void FindCaseSensitiveExactMatch()
    {
        var doc = ParseDoc(LibraryXml);

        // With matchCase=true, "BOOK" should not match "book".
        var upperMatches = FindService.Find(doc, "BOOK", matchCase: true);
        Assert.Empty(upperMatches);

        // But "book" must still match.
        var lowerMatches = FindService.Find(doc, "book", matchCase: true);
        Assert.Equal(2, lowerMatches.Count);
    }

    [Fact]
    public void FindEmptyQueryReturnsEmpty()
    {
        var doc = ParseDoc(LibraryXml);

        Assert.Empty(FindService.Find(doc, "", matchCase: false));
        Assert.Empty(FindService.Find(doc, null, matchCase: false));
    }

    [Fact]
    public void FindReturnsDocumentOrder()
    {
        // A document whose element order is deterministic.
        var doc = ParseDoc("<root><alpha/><beta/><alpha/></root>");

        var matches = FindService.Find(doc, "alpha", matchCase: false);

        // Expect two matches, and the first resolved element must come before the second in
        // the Children list of the root element.
        Assert.Equal(2, matches.Count);
        Assert.NotEqual(matches[0], matches[1]);

        // Verify that both resolve to XdmElement named "alpha".
        Assert.All(matches, id =>
        {
            var node = doc.Resolve(id);
            Assert.IsType<XdmElement>(node);
            Assert.Equal("alpha", ((XdmElement)node).LocalName);
        });
    }

    [Fact]
    public void FindNoMatchesReturnsEmpty()
    {
        var doc = ParseDoc(LibraryXml);

        var matches = FindService.Find(doc, "zzznomatch", matchCase: false);

        Assert.Empty(matches);
    }
}
