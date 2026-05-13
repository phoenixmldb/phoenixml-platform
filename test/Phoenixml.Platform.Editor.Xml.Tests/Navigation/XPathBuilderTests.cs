using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Navigation;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Xunit;

namespace Phoenixml.Platform.Editor.Xml.Tests.Navigation;

public class XPathBuilderTests
{
    private static async Task<XmlDocument> LoadAsync(string xml)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.xml");
        await File.WriteAllTextAsync(path, xml, TestContext.Current.CancellationToken).ConfigureAwait(true);
        return await XmlDocument.LoadAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    [Fact]
    public async Task BuildForDocumentReturnsSlash()
    {
        var doc = await LoadAsync("<library><book/></library>");
        var xpath = XPathBuilder.Build(doc, doc.Root.Id);
        Assert.Equal("/", xpath);
    }

    [Fact]
    public async Task BuildForRootElementReturnsRootName()
    {
        var doc = await LoadAsync("<library><book/></library>");
        var rootId = doc.Root.DocumentElement!.Value;
        var xpath = XPathBuilder.Build(doc, rootId);
        Assert.Equal("/library", xpath);
    }

    [Fact]
    public async Task BuildForSingleChildOmitsPredicate()
    {
        var doc = await LoadAsync("<library><book/></library>");
        var rootEl = (XdmElement)doc.Resolve(doc.Root.DocumentElement!.Value)!;
        var bookId = rootEl.Children.Single();
        var xpath = XPathBuilder.Build(doc, bookId);
        Assert.Equal("/library/book", xpath);
    }

    [Fact]
    public async Task BuildForRepeatedChildEmitsPredicate()
    {
        var doc = await LoadAsync("<library><book/><book/><book/></library>");
        var rootEl = (XdmElement)doc.Resolve(doc.Root.DocumentElement!.Value)!;
        var secondBookId = rootEl.Children[1];
        var xpath = XPathBuilder.Build(doc, secondBookId);
        Assert.Equal("/library/book[2]", xpath);
    }

    [Fact]
    public async Task BuildForAttributeEmitsAtName()
    {
        var doc = await LoadAsync("<library><book isbn=\"123\"/></library>");
        var rootEl = (XdmElement)doc.Resolve(doc.Root.DocumentElement!.Value)!;
        var book = (XdmElement)doc.Resolve(rootEl.Children.Single())!;
        var attrId = book.Attributes.Single();
        var xpath = XPathBuilder.Build(doc, attrId);
        Assert.Equal("/library/book/@isbn", xpath);
    }

    [Fact]
    public async Task BuildForAttributeOnRepeatedElementIncludesParentPredicate()
    {
        var doc = await LoadAsync("<library><book/><book isbn=\"x\"/></library>");
        var rootEl = (XdmElement)doc.Resolve(doc.Root.DocumentElement!.Value)!;
        var secondBook = (XdmElement)doc.Resolve(rootEl.Children[1])!;
        var attrId = secondBook.Attributes.Single();
        var xpath = XPathBuilder.Build(doc, attrId);
        Assert.Equal("/library/book[2]/@isbn", xpath);
    }

    [Fact]
    public async Task BuildForDeeplyNestedElement()
    {
        var doc = await LoadAsync("<library><book/><book><title>T</title></book></library>");
        var rootEl = (XdmElement)doc.Resolve(doc.Root.DocumentElement!.Value)!;
        var secondBook = (XdmElement)doc.Resolve(rootEl.Children[1])!;
        var titleId = secondBook.Children.OfType<NodeId>().First(c =>
            doc.Resolve(c) is XdmElement el && el.LocalName == "title");
        var xpath = XPathBuilder.Build(doc, titleId);
        Assert.Equal("/library/book[2]/title", xpath);
    }

    [Fact]
    public async Task BuildForTextNodeEmitsTextStep()
    {
        var doc = await LoadAsync("<library><book>hello</book></library>");
        var rootEl = (XdmElement)doc.Resolve(doc.Root.DocumentElement!.Value)!;
        var book = (XdmElement)doc.Resolve(rootEl.Children.Single())!;
        var textId = book.Children.First(c => doc.Resolve(c) is XdmText);
        var xpath = XPathBuilder.Build(doc, textId);
        Assert.Equal("/library/book/text()", xpath);
    }

    [Fact]
    public async Task BuildForCommentEmitsCommentStep()
    {
        var doc = await LoadAsync("<library><!-- hi --><book/></library>");
        var rootEl = (XdmElement)doc.Resolve(doc.Root.DocumentElement!.Value)!;
        var commentId = rootEl.Children.First(c => doc.Resolve(c) is XdmComment);
        var xpath = XPathBuilder.Build(doc, commentId);
        Assert.Equal("/library/comment()", xpath);
    }

    [Fact]
    public async Task BuildForProcessingInstructionEmitsPiStep()
    {
        var doc = await LoadAsync("<library><?xml-stylesheet href=\"a\"?><book/></library>");
        var rootEl = (XdmElement)doc.Resolve(doc.Root.DocumentElement!.Value)!;
        var piId = rootEl.Children.First(c => doc.Resolve(c) is XdmProcessingInstruction);
        var xpath = XPathBuilder.Build(doc, piId);
        Assert.Equal("/library/processing-instruction('xml-stylesheet')", xpath);
    }

    [Fact]
    public async Task BuildForUnresolvedNodeReturnsEmpty()
    {
        var doc = await LoadAsync("<library/>");
        // NodeId is a strongly-typed wrapper; pick one we know is unallocated.
        var bogus = new NodeId(ulong.MaxValue);
        var xpath = XPathBuilder.Build(doc, bogus);
        Assert.Equal(string.Empty, xpath);
    }
}
