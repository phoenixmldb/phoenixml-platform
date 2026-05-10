using System.Collections.Generic;
using System.Linq;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Editing;
using Phoenixml.Platform.Editor.Xml.Find;
using Xunit;
using PlatformDocumentId = Phoenixml.Platform.Abstractions.Documents.DocumentId;

namespace Phoenixml.Platform.Editor.Xml.Tests.Find;

/// <summary>
/// Unit tests for <see cref="ReplaceService"/>.
/// </summary>
public class ReplaceServiceTests
{
    // Shared fixture used by most tests.
    //   <library>
    //     <book isbn="book-isbn-001">The C Programming Language</book>
    //     <book isbn="book-isbn-002">Effective Java</book>
    //   </library>
    private const string LibraryXml =
        "<library>" +
        "<book isbn=\"book-isbn-001\">The C Programming Language</book>" +
        "<book isbn=\"book-isbn-002\">Effective Java</book>" +
        "</library>";

    private static XmlDocument ParseDoc(string xml)
    {
        var (result, nsTable) = XmlDocument.ParseXml(xml);
        return new XmlDocument(
            PlatformDocumentId.New(),
            source: null,
            displayName: "test.xml",
            result,
            nsTable);
    }

    // Gets element text content (single text child value).
    private static string GetText(XmlDocument doc, NodeId elemId)
    {
        var elem = (XdmElement)doc.Resolve(elemId)!;
        var textId = elem.Children.First(id => doc.Resolve(id) is XdmText);
        return ((XdmText)doc.Resolve(textId)!).Value;
    }

    [Fact]
    public void BuildReplacementsForElementName()
    {
        var doc = ParseDoc(LibraryXml);
        var matches = FindService.Find(doc, "book", matchCase: true);
        // Finds both <book> elements (plus possible text/attr hits — filter to elements only).
        var elementMatches = matches.Where(id => doc.Resolve(id) is XdmElement).ToList();
        Assert.NotEmpty(elementMatches);

        var commands = ReplaceService.BuildReplacements(
            doc, elementMatches, "book", "tome", matchCase: true);

        Assert.Equal(elementMatches.Count, commands.Count);
        Assert.All(commands, cmd => Assert.IsType<RenameElementCommand>(cmd));

        // Apply and verify.
        foreach (var cmd in commands)
            doc.Undo.Execute(cmd);

        Assert.All(elementMatches, id =>
            Assert.Equal("tome", ((XdmElement)doc.Resolve(id)!).LocalName));
    }

    [Fact]
    public void BuildReplacementsForAttributeName()
    {
        // Craft a document where the query matches attribute name ONLY (not the value).
        const string xml = "<root><item kind=\"value-does-not-match\">text</item></root>";
        var doc = ParseDoc(xml);

        // "kind" appears only in the attribute local name, not in "value-does-not-match".
        var matches = FindService.Find(doc, "kind", matchCase: true);
        var attrMatches = matches.Where(id => doc.Resolve(id) is XdmAttribute).ToList();
        Assert.Single(attrMatches);

        var commands = ReplaceService.BuildReplacements(
            doc, attrMatches, "kind", "type", matchCase: true);

        // Only a rename command — value doesn't change.
        Assert.Single(commands);
        Assert.IsType<RenameAttributeCommand>(commands[0]);
    }

    [Fact]
    public void BuildReplacementsForAttributeValue()
    {
        // Value contains "isbn"; query "isbn", replacement "ref".
        var doc = ParseDoc(LibraryXml);
        var matches = FindService.Find(doc, "isbn", matchCase: true);
        var attrMatches = matches.Where(id => doc.Resolve(id) is XdmAttribute).ToList();
        Assert.NotEmpty(attrMatches);

        // Patch: feed each attribute match but with a query that ONLY hits the value.
        // "book-isbn" is only in the value, not the local name "isbn".
        var valueCommands = ReplaceService.BuildReplacements(
            doc, attrMatches, "book-isbn", "ref", matchCase: true);

        // Each attribute has value "book-isbn-00x" — the local name "isbn" doesn't contain "book-isbn".
        // So only SetAttributeValueCommands should be generated.
        Assert.All(valueCommands, cmd => Assert.IsType<SetAttributeValueCommand>(cmd));
        Assert.Equal(attrMatches.Count, valueCommands.Count);
    }

    [Fact]
    public void BuildReplacementsForTextContent()
    {
        var doc = ParseDoc(LibraryXml);
        // "Java" only appears in text content.
        var matches = FindService.Find(doc, "Java", matchCase: true);
        var textMatches = matches.Where(id => doc.Resolve(id) is XdmText).ToList();
        Assert.Single(textMatches);

        var commands = ReplaceService.BuildReplacements(
            doc, textMatches, "Java", "Python", matchCase: true);

        Assert.Single(commands);
        Assert.IsType<SetTextContentCommand>(commands[0]);

        // Apply and verify.
        doc.Undo.Execute(commands[0]);

        // Find the <book> element whose text previously contained "Java".
        var textNode = (XdmText)doc.Resolve(textMatches[0])!;
        // Resolve the parent element and check its new text.
        var parentId = textNode.Parent!.Value;
        Assert.Contains("Python", GetText(doc, parentId), System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReplacementsForAttributeWithBothNameAndValueMatch()
    {
        // Craft a document where an attribute local name and its value both contain the query.
        const string xml = "<root><item tag=\"tag-value\">text</item></root>";
        var doc = ParseDoc(xml);

        // "tag" appears in both the local name ("tag") and the value ("tag-value").
        var matches = FindService.Find(doc, "tag", matchCase: true);
        var attrMatches = matches.Where(id => doc.Resolve(id) is XdmAttribute).ToList();
        Assert.Single(attrMatches);

        var commands = ReplaceService.BuildReplacements(
            doc, attrMatches, "tag", "label", matchCase: true);

        // Should produce two commands: rename-attribute then set-attribute-value.
        Assert.Equal(2, commands.Count);
        Assert.IsType<RenameAttributeCommand>(commands[0]);
        Assert.IsType<SetAttributeValueCommand>(commands[1]);

        // Apply both and verify the attribute was updated correctly.
        foreach (var cmd in commands)
            doc.Undo.Execute(cmd);

        var attrId = attrMatches[0];
        var attr = (XdmAttribute)doc.Resolve(attrId)!;
        Assert.Equal("label", attr.LocalName);
        Assert.Equal("label-value", attr.Value);
    }

    [Fact]
    public void BuildReplacementsCaseSensitiveOnlyMatchesExactCase()
    {
        var doc = ParseDoc(LibraryXml);

        // "BOOK" (upper) — with matchCase=true, should not hit element local name "book" (lower).
        var matches = FindService.Find(doc, "book", matchCase: true);
        var elementMatches = matches.Where(id => doc.Resolve(id) is XdmElement).ToList();

        var commands = ReplaceService.BuildReplacements(
            doc, elementMatches, "BOOK", "tome", matchCase: true);

        // No commands because the local name is lowercase "book", not "BOOK".
        Assert.Empty(commands);
    }

    [Fact]
    public void BuildReplacementsCaseInsensitiveMatchesAnyCase()
    {
        var doc = ParseDoc(LibraryXml);

        // Find with case-insensitive.
        var matches = FindService.Find(doc, "BOOK", matchCase: false);
        var elementMatches = matches.Where(id => doc.Resolve(id) is XdmElement).ToList();
        Assert.NotEmpty(elementMatches);

        // Replace with matchCase=false should still hit "book".
        var commands = ReplaceService.BuildReplacements(
            doc, elementMatches, "BOOK", "tome", matchCase: false);

        Assert.Equal(elementMatches.Count, commands.Count);
        Assert.All(commands, cmd => Assert.IsType<RenameElementCommand>(cmd));
    }

    [Fact]
    public void BuildReplacementsEmptyQueryReturnsEmpty()
    {
        var doc = ParseDoc(LibraryXml);
        var matches = FindService.Find(doc, "book", matchCase: false);

        var commands = ReplaceService.BuildReplacements(
            doc, matches, query: "", replacement: "tome", matchCase: false);

        Assert.Empty(commands);
    }

    [Fact]
    public void BuildReplacementsWhenSubstringNotPresentReturnsEmpty()
    {
        var doc = ParseDoc(LibraryXml);

        // Grab an element NodeId, then query for something that isn't in its local name.
        var matches = FindService.Find(doc, "book", matchCase: true);
        var elementMatches = matches.Where(id => doc.Resolve(id) is XdmElement).ToList();
        Assert.NotEmpty(elementMatches);

        // "zzz" is not in "book" — should produce no commands (stale-match scenario).
        var commands = ReplaceService.BuildReplacements(
            doc, elementMatches, "zzz", "replaced", matchCase: true);

        Assert.Empty(commands);
    }

    [Fact]
    public void BuildReplacementsEmptyReplacementRemovesSubstring()
    {
        // Craft a document with an element whose local name has a prefix segment.
        const string xml = "<root><xsi-data>value</xsi-data></root>";
        var doc = ParseDoc(xml);

        var matches = FindService.Find(doc, "xsi-", matchCase: true);
        var elementMatches = matches.Where(id => doc.Resolve(id) is XdmElement).ToList();
        Assert.Single(elementMatches);

        // Replace "xsi-" with "" to strip the prefix.
        var commands = ReplaceService.BuildReplacements(
            doc, elementMatches, "xsi-", replacement: "", matchCase: true);

        Assert.Single(commands);
        Assert.IsType<RenameElementCommand>(commands[0]);

        doc.Undo.Execute(commands[0]);

        var renamed = (XdmElement)doc.Resolve(elementMatches[0])!;
        Assert.Equal("data", renamed.LocalName);
    }
}
