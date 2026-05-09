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

public class RenameElementCommandTests
{
    private const string CatalogXml =
        "<catalog>" +
        "<product id=\"p1\" featured=\"true\"><name>Widget</name></product>" +
        "<product id=\"p2\"><name>Gadget</name></product>" +
        "</catalog>";

    private static (XmlDocument Doc, NodeId RootId, NodeId ProductId) BuildDoc()
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
        var productId = rootElem.Children.First(id =>
            doc.Resolve(id) is XdmElement);

        return (doc, rootId, productId);
    }

    [Fact]
    public void ApplyRenamesElementInPlace()
    {
        var (doc, rootId, _) = BuildDoc();
        var cmd = new RenameElementCommand(doc, rootId, "products");

        doc.Undo.Execute(cmd);

        var renamed = Assert.IsType<XdmElement>(doc.Resolve(rootId));
        Assert.Equal("products", renamed.LocalName);
    }

    [Fact]
    public void RevertRestoresOriginalName()
    {
        var (doc, rootId, _) = BuildDoc();
        var cmd = new RenameElementCommand(doc, rootId, "products");

        doc.Undo.Execute(cmd);
        doc.Undo.Undo();

        var restored = Assert.IsType<XdmElement>(doc.Resolve(rootId));
        Assert.Equal("catalog", restored.LocalName);
    }

    [Fact]
    public void ApplyKeepsChildrenAndAttributes()
    {
        var (doc, _, productId) = BuildDoc();

        var before = Assert.IsType<XdmElement>(doc.Resolve(productId));
        var attrCountBefore = before.Attributes.Count;
        var childCountBefore = before.Children.Count;

        var cmd = new RenameElementCommand(doc, productId, "item");
        doc.Undo.Execute(cmd);

        var after = Assert.IsType<XdmElement>(doc.Resolve(productId));
        Assert.Equal("item", after.LocalName);
        Assert.Equal(attrCountBefore, after.Attributes.Count);
        Assert.Equal(childCountBefore, after.Children.Count);
    }

    [Fact]
    public void ApplyKeepsNodeId()
    {
        var (doc, rootId, _) = BuildDoc();
        var cmd = new RenameElementCommand(doc, rootId, "products");

        doc.Undo.Execute(cmd);

        var after = Assert.IsType<XdmElement>(doc.Resolve(rootId));
        Assert.Equal(rootId, after.Id);
    }

    [Fact]
    public void TryMergeWithTwoConsecutiveRenamesOnSameElementMerge()
    {
        // Execute two renames on the same element via UndoStack so TryMergeWith is invoked.
        // UndoStack merges consecutive commands on the same element into one undo step.
        var (doc, rootId, _) = BuildDoc();

        var first = new RenameElementCommand(doc, rootId, "foo");
        doc.Undo.Execute(first);

        var second = new RenameElementCommand(doc, rootId, "bar");
        doc.Undo.Execute(second);

        // After two executes, the undo stack should have ONE merged step.
        // Undoing once should restore "catalog" (not "foo").
        Assert.True(doc.Undo.CanUndo);
        doc.Undo.Undo();

        var reverted = Assert.IsType<XdmElement>(doc.Resolve(rootId));
        Assert.Equal("catalog", reverted.LocalName);

        // Confirm there is now nothing left to undo (only one merged step was on the stack).
        Assert.False(doc.Undo.CanUndo);
    }

    [Fact]
    public void TryMergeWithDifferentElementsDoNotMerge()
    {
        var (doc, rootId, productId) = BuildDoc();

        var renameRoot = new RenameElementCommand(doc, rootId, "products");
        doc.Undo.Execute(renameRoot);

        var renameProduct = new RenameElementCommand(doc, productId, "item");
        doc.Undo.Execute(renameProduct);

        // Two different elements — the stack should have two undo steps.
        Assert.True(doc.Undo.CanUndo);
        doc.Undo.Undo();  // reverts product rename
        Assert.True(doc.Undo.CanUndo);
        doc.Undo.Undo();  // reverts root rename

        var rootRestored = Assert.IsType<XdmElement>(doc.Resolve(rootId));
        Assert.Equal("catalog", rootRestored.LocalName);
    }

    [Fact]
    public async Task ApplyThenSerializeProducesNewName()
    {
        var (doc, rootId, _) = BuildDoc();
        var cmd = new RenameElementCommand(doc, rootId, "products");
        doc.Undo.Execute(cmd);

        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "output.xml");
        try
        {
            await doc.SaveAsAsync(new Uri(path), TestContext.Current.CancellationToken).ConfigureAwait(true);

            var reloaded = await XmlDocument.LoadAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
            var reloadedRootId = reloaded.Root.DocumentElement!.Value;
            var reloadedRoot = Assert.IsType<XdmElement>(reloaded.Resolve(reloadedRootId));
            Assert.Equal("products", reloadedRoot.LocalName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
