using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Editing;
using Phoenixml.Platform.Editor.Xml.Tree;
using Xunit;
using PlatformDocumentId = Phoenixml.Platform.Abstractions.Documents.DocumentId;

namespace Phoenixml.Platform.Editor.Xml.Tests.Tree;

public class XmlTreeNodeProviderTests
{
    private const string SimpleXml =
        "<root id=\"1\"><child attr=\"hello\">text content</child></root>";

    // Helper — not a test method, so ConfigureAwait(false) is appropriate here
    private static async Task<XmlDocument> LoadDocFromXml(string xml, System.Threading.CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.xml");
        await File.WriteAllTextAsync(path, xml, ct).ConfigureAwait(false);
        return await XmlDocument.LoadAsync(path, ct).ConfigureAwait(false);
    }

    [Fact]
    public async Task RootsHasSingleDocumentNode()
    {
        var doc = await LoadDocFromXml(SimpleXml, TestContext.Current.CancellationToken).ConfigureAwait(true);
        var provider = doc.TreeNodes;

        Assert.Single(provider.Roots);
        var root = Assert.IsType<XmlTreeNode>(provider.Roots[0]);
        Assert.IsType<XdmDocument>(root.XdmNode);
    }

    [Fact]
    public async Task GetChildrenOnDocumentReturnsRootElement()
    {
        var doc = await LoadDocFromXml(SimpleXml, TestContext.Current.CancellationToken).ConfigureAwait(true);
        var provider = doc.TreeNodes;

        var docNode = provider.Roots[0];
        var children = provider.GetChildren(docNode);

        Assert.NotEmpty(children);
        var firstChild = Assert.IsType<XmlTreeNode>(children[0]);
        var elem = Assert.IsType<XdmElement>(firstChild.XdmNode);
        Assert.Equal("root", elem.LocalName);
    }

    [Fact]
    public async Task GetChildrenOnElementReturnsAttributesThenChildren()
    {
        var doc = await LoadDocFromXml(SimpleXml, TestContext.Current.CancellationToken).ConfigureAwait(true);
        var provider = doc.TreeNodes;

        // Get to the root element
        var docNode = provider.Roots[0];
        var rootElemNode = provider.GetChildren(docNode)[0];
        var children = provider.GetChildren(rootElemNode);

        // <root id="1"> has 1 attribute + 1 child element
        Assert.Equal(2, children.Count);

        // First should be the attribute @id
        var attrNode = Assert.IsType<XmlTreeNode>(children[0]);
        Assert.IsType<XdmAttribute>(attrNode.XdmNode);
        Assert.Equal("@id", attrNode.Label);

        // Second should be the child element <child>
        var childElemNode = Assert.IsType<XmlTreeNode>(children[1]);
        var childElem = Assert.IsType<XdmElement>(childElemNode.XdmNode);
        Assert.Equal("child", childElem.LocalName);
    }

    [Fact]
    public async Task LabelFormatsByNodeKind()
    {
        const string xml = "<!-- comment --><root><?pi data?><child>text</child></root>";
        var doc = await LoadDocFromXml(xml, TestContext.Current.CancellationToken).ConfigureAwait(true);
        var provider = doc.TreeNodes;

        // Document node label — when a file URI is set, it shows the URI; otherwise "(document)"
        var docNode = Assert.IsType<XmlTreeNode>(provider.Roots[0]);
        Assert.NotEmpty(docNode.Label);

        // Navigate to root element
        var children = provider.GetChildren(docNode);
        XmlTreeNode rootElemNode = children
            .OfType<XmlTreeNode>()
            .First(n => n.XdmNode is XdmElement el && el.LocalName == "root");
        Assert.Equal("root", rootElemNode.Label);

        // Check comment label (may appear before root element at document level)
        var commentNode = children.OfType<XmlTreeNode>().FirstOrDefault(n => n.XdmNode is XdmComment);
        if (commentNode is not null)
            Assert.StartsWith("<!--", commentNode.Label, StringComparison.Ordinal);

        // Check PI inside root
        var rootChildren = provider.GetChildren(rootElemNode);
        var piNode = rootChildren.OfType<XmlTreeNode>().FirstOrDefault(n => n.XdmNode is XdmProcessingInstruction);
        if (piNode is not null)
            Assert.StartsWith("<?", piNode.Label, StringComparison.Ordinal);

        // Text node label
        var childElemNode = rootChildren.OfType<XmlTreeNode>()
            .First(n => n.XdmNode is XdmElement el && el.LocalName == "child");
        var textNodes = provider.GetChildren(childElemNode).OfType<XmlTreeNode>()
            .Where(n => n.XdmNode is XdmText).ToList();
        if (textNodes.Count > 0)
            Assert.StartsWith("\"", textNodes[0].Label, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HasChildrenTrueForElementsWithChildrenFalseForLeaves()
    {
        const string xml = "<root><leaf/></root>";
        var doc = await LoadDocFromXml(xml, TestContext.Current.CancellationToken).ConfigureAwait(true);
        var provider = doc.TreeNodes;

        var docNode = provider.Roots[0];
        Assert.True(docNode.HasChildren);

        var rootElemNode = provider.GetChildren(docNode)[0];
        Assert.True(rootElemNode.HasChildren);

        var leafNode = provider.GetChildren(rootElemNode)[0];
        Assert.False(leafNode.HasChildren);
    }

    [Fact]
    public void ChangedEventFiresWhenRenameCommandExecutesViaUndoStack()
    {
        // Arrange: build doc without a file path (no async needed)
        var (result, nsTable) = XmlDocument.ParseXml(SimpleXml);
        var doc = new XmlDocument(
            PlatformDocumentId.New(),
            source: null,
            "test.xml",
            result,
            nsTable);

        var provider = doc.TreeNodes;

        var firedCount = 0;
        provider.Changed += () => firedCount++;

        var rootId = result.Document.DocumentElement!.Value;
        var cmd = new RenameElementCommand(doc, rootId, "renamed");

        // Act
        doc.Undo.Execute(cmd);

        // Assert: Changed fired once
        Assert.Equal(1, firedCount);
    }
}
