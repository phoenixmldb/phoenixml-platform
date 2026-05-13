using System.Linq;
using System.Threading.Tasks;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml.Find;
using Xunit;

namespace Phoenixml.Platform.Editor.Xml.Tests.Find;

/// <summary>
/// Unit tests for <see cref="XPathQueryService"/>. Parses inline XML and verifies that
/// XPath expressions resolve to the expected <see cref="PhoenixmlDb.Core.NodeId"/>s.
/// </summary>
public class XPathQueryServiceTests
{
    private const string LibraryXml = """
        <library>
          <book isbn="978-0-13-110362-7">
            <title>The C Programming Language</title>
            <author>Kernighan</author>
            <year>1978</year>
          </book>
          <book>
            <title>Effective Java</title>
            <author>Bloch</author>
            <year>2001</year>
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

    private static Task<XPathResult> Eval(XmlDocument doc, string xpath) =>
        XPathQueryService.EvaluateAsync(doc, xpath, TestContext.Current.CancellationToken);

    [Fact]
    public async Task EvaluateSimpleAbsolutePath()
    {
        var doc = ParseDoc(LibraryXml);

        var r = await Eval(doc, "/library");

        Assert.Null(r.Error);
        Assert.Single(r.Nodes);
        Assert.IsType<XdmElement>(doc.Resolve(r.Nodes[0]));
        Assert.Equal("library", ((XdmElement)doc.Resolve(r.Nodes[0])!).LocalName);
    }

    [Fact]
    public async Task EvaluateDescendantAxis()
    {
        var doc = ParseDoc(LibraryXml);

        var r = await Eval(doc, "//book");

        Assert.Null(r.Error);
        Assert.Equal(2, r.Nodes.Count);
        Assert.All(r.Nodes, id => Assert.Equal("book", ((XdmElement)doc.Resolve(id)!).LocalName));
    }

    [Fact]
    public async Task EvaluateWithPredicate()
    {
        var doc = ParseDoc(LibraryXml);

        var r = await Eval(doc, "//book[year='1978']");

        Assert.Null(r.Error);
        Assert.Single(r.Nodes);
    }

    [Fact]
    public async Task EvaluateAttributeAxis()
    {
        var doc = ParseDoc(LibraryXml);

        var r = await Eval(doc, "//@isbn");

        Assert.Null(r.Error);
        Assert.Single(r.Nodes);
        Assert.IsType<XdmAttribute>(doc.Resolve(r.Nodes[0]));
    }

    [Fact]
    public async Task EvaluateIndexedChild()
    {
        var doc = ParseDoc(LibraryXml);

        var r = await Eval(doc, "//book[2]/title");

        Assert.Null(r.Error);
        Assert.Single(r.Nodes);
        var title = (XdmElement)doc.Resolve(r.Nodes[0])!;
        Assert.Equal("title", title.LocalName);
    }

    [Fact]
    public async Task EvaluateInvalidXPathReturnsError()
    {
        var doc = ParseDoc(LibraryXml);

        var r = await Eval(doc, "//[broken");

        Assert.NotNull(r.Error);
        Assert.Empty(r.Nodes);
    }

    [Fact]
    public async Task EvaluateNoMatchesReturnsEmpty()
    {
        var doc = ParseDoc(LibraryXml);

        var r = await Eval(doc, "//nonexistent");

        Assert.Null(r.Error);
        Assert.Empty(r.Nodes);
    }

    [Fact]
    public async Task EvaluateEmptyExpressionIsNoOp()
    {
        var doc = ParseDoc(LibraryXml);

        var r = await Eval(doc, "   ");

        Assert.Null(r.Error);
        Assert.Empty(r.Nodes);
    }

    [Fact]
    public async Task EvaluateScalarResultProducesNoNodes()
    {
        // count() returns an atomic — for Plan 21 v0.12 we only highlight nodes.
        // The service must not throw and must return an empty node list.
        var doc = ParseDoc(LibraryXml);

        var r = await Eval(doc, "count(//book)");

        Assert.Null(r.Error);
        Assert.Empty(r.Nodes);
    }
}
