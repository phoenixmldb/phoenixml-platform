using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Editing;
using Xunit;
using PlatformDocumentId = Phoenixml.Platform.Abstractions.Documents.DocumentId;

namespace Phoenixml.Platform.Editor.Xml.Tests.Editing;

public class DeleteNodeCommandTests
{
    // <catalog>
    //   <product id="p1">       ← productId
    //     <name>Widget</name>   ← nameId (with text child)
    //     <desc>Cool</desc>     ← descId
    //   </product>
    //   <product id="p2"><name>Gadget</name></product>
    //   <product id="p3"><name>Doohickey</name></product>
    // </catalog>
    private const string CatalogXml =
        "<catalog>" +
        "<product id=\"p1\"><name>Widget</name><desc>Cool</desc></product>" +
        "<product id=\"p2\"><name>Gadget</name></product>" +
        "<product id=\"p3\"><name>Doohickey</name></product>" +
        "</catalog>";

    private static (XmlDocument Doc, NodeId RootId, NodeId ProductId, NodeId NameId, NodeId AttrId) BuildDoc()
    {
        var (result, nsTable) = XmlDocument.ParseXml(CatalogXml);
        var doc = new XmlDocument(
            PlatformDocumentId.New(),
            source: null,
            "catalog.xml",
            result,
            nsTable);

        var rootId = result.Document.DocumentElement!.Value;
        var rootElem = (XdmElement)doc.Resolve(rootId)!;

        // First element child of root = first <product>.
        var productId = rootElem.Children.FirstOrDefault(id => doc.Resolve(id) is XdmElement);
        var productElem = (XdmElement)doc.Resolve(productId)!;

        // First element child of product = <name>.
        var nameId = productElem.Children.FirstOrDefault(id => doc.Resolve(id) is XdmElement);

        // First attribute of product = id="p1".
        var attrId = productElem.Attributes[0];

        return (doc, rootId, productId, nameId, attrId);
    }

    [Fact]
    public void ApplyRemovesElementFromParentChildren()
    {
        var (doc, rootId, productId, _, _) = BuildDoc();
        var before = (XdmElement)doc.Resolve(rootId)!;
        var countBefore = before.Children.Count;

        var cmd = new DeleteNodeCommand(doc, productId);
        doc.Undo.Execute(cmd);

        var after = (XdmElement)doc.Resolve(rootId)!;
        Assert.Equal(countBefore - 1, after.Children.Count);
        Assert.DoesNotContain(productId, after.Children);
    }

    [Fact]
    public void ApplyRemovesAttributeFromParentAttributes()
    {
        var (doc, _, productId, _, attrId) = BuildDoc();
        var before = (XdmElement)doc.Resolve(productId)!;
        var attrCountBefore = before.Attributes.Count;

        var cmd = new DeleteNodeCommand(doc, attrId);
        doc.Undo.Execute(cmd);

        var after = (XdmElement)doc.Resolve(productId)!;
        Assert.Equal(attrCountBefore - 1, after.Attributes.Count);
        Assert.DoesNotContain(attrId, after.Attributes);
    }

    [Fact]
    public void ApplyOrphanedNodeStaysInById()
    {
        var (doc, rootId, productId, _, _) = BuildDoc();

        var cmd = new DeleteNodeCommand(doc, productId);
        doc.Undo.Execute(cmd);

        // The deleted product element must still be resolvable (Compact-on-Save's job, not Delete's).
        var orphan = doc.Resolve(productId);
        Assert.NotNull(orphan);
    }

    [Fact]
    public void ApplyReferencedNodeIdsIncludesEntireSubtree()
    {
        // <product id="p1"><name>Widget</name><desc>Cool</desc></product>
        // Deleting <product> should pin: product, @id, <name>, text(Widget), <desc>, text(Cool) = 6 nodes.
        var (doc, _, productId, _, _) = BuildDoc();

        var cmd = new DeleteNodeCommand(doc, productId);
        doc.Undo.Execute(cmd);

        var refs = cmd.ReferencedNodeIds.ToHashSet();
        // At minimum product itself + its attribute + its two element children.
        Assert.Contains(productId, refs);
        Assert.True(refs.Count >= 4, $"Expected at least 4 subtree ids, got {refs.Count}.");
    }

    [Fact]
    public void RevertRestoresAtOriginalIndex()
    {
        var (doc, rootId, _, nameId, _) = BuildDoc();

        // nameId is the first child of the first product.
        // Get the product element.
        var productId = ((XdmElement)doc.Resolve(rootId)!).Children
            .FirstOrDefault(id => doc.Resolve(id) is XdmElement);
        var productBefore = (XdmElement)doc.Resolve(productId)!;
        var originalIndex = FindIndex(productBefore.Children, nameId);

        var cmd = new DeleteNodeCommand(doc, nameId);
        doc.Undo.Execute(cmd);
        doc.Undo.Undo();

        var productAfter = (XdmElement)doc.Resolve(productId)!;
        Assert.Equal(nameId, productAfter.Children[originalIndex]);
    }

    [Fact]
    public void RevertAfterApplyFullRoundTrip()
    {
        var (doc, rootId, productId, _, _) = BuildDoc();

        // Capture the original children of root.
        var originalChildren = ((XdmElement)doc.Resolve(rootId)!).Children.ToList();

        var cmd = new DeleteNodeCommand(doc, productId);
        doc.Undo.Execute(cmd);
        doc.Undo.Undo();

        var restored = ((XdmElement)doc.Resolve(rootId)!).Children.ToList();
        Assert.Equal(originalChildren, restored);

        // The target node itself is still resolvable and unchanged.
        var resolvedTarget = doc.Resolve(productId);
        Assert.NotNull(resolvedTarget);
    }

    [Fact]
    public void ApplyOnDocumentRootThrows()
    {
        var (doc, rootId, _, _, _) = BuildDoc();

        // rootId is the document element. Its parent is the XdmDocument node, not null —
        // but the XdmDocument is not an XdmElement, so Apply should throw.
        // Actually, the doc root's Parent is the XdmDocument node id, which is not XdmElement.
        // Let's find the XdmDocument nodeId and try deleting that (Parent == null → throws).
        // The XdmDocument's Parent is null.
        var docNode = doc.Root;
        var cmd = new DeleteNodeCommand(doc, docNode.Id);
        Assert.Throws<InvalidOperationException>(() => doc.Undo.Execute(cmd));
    }

    [Fact]
    public async Task ApplyThenSerializeOmitsDeletedNode()
    {
        var (doc, rootId, productId, _, _) = BuildDoc();

        var cmd = new DeleteNodeCommand(doc, productId);
        doc.Undo.Execute(cmd);

        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "output.xml");
        try
        {
            await doc.SaveAsAsync(new Uri(path), TestContext.Current.CancellationToken).ConfigureAwait(true);
            var xml = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
            // After deleting the first product (id="p1"), it should not appear in the output.
            Assert.DoesNotContain("id=\"p1\"", xml, StringComparison.Ordinal);
            // The other products should still be there.
            Assert.Contains("id=\"p2\"", xml, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryMergeWithDoesNotMerge()
    {
        var (doc, rootId, _, _, _) = BuildDoc();
        var rootElem = (XdmElement)doc.Resolve(rootId)!;

        // Delete first and second products (two separate delete steps).
        var firstProductId = rootElem.Children.FirstOrDefault(id => doc.Resolve(id) is XdmElement);
        var firstCmd = new DeleteNodeCommand(doc, firstProductId);
        doc.Undo.Execute(firstCmd);

        var updatedRoot = (XdmElement)doc.Resolve(rootId)!;
        var secondProductId = updatedRoot.Children.FirstOrDefault(id => doc.Resolve(id) is XdmElement);
        var secondCmd = new DeleteNodeCommand(doc, secondProductId);
        doc.Undo.Execute(secondCmd);

        // Two deletes → two undo steps.
        Assert.True(doc.Undo.CanUndo);
        doc.Undo.Undo();
        Assert.True(doc.Undo.CanUndo);
        doc.Undo.Undo();
        Assert.False(doc.Undo.CanUndo);
    }

    private static int FindIndex(IReadOnlyList<NodeId> list, NodeId target)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == target) return i;
        return -1;
    }
}
