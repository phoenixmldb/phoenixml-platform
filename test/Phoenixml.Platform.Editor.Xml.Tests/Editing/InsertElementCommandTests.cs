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

public class InsertElementCommandTests
{
    private const string CatalogXml =
        "<catalog>" +
        "<product id=\"p1\"><name>Widget</name></product>" +
        "<product id=\"p2\"><name>Gadget</name></product>" +
        "<product id=\"p3\"><name>Doohickey</name></product>" +
        "</catalog>";

    private static (XmlDocument Doc, NodeId RootId) BuildDoc()
    {
        var (result, nsTable) = XmlDocument.ParseXml(CatalogXml);
        var doc = new XmlDocument(
            PlatformDocumentId.New(),
            source: null,
            "catalog.xml",
            result,
            nsTable);
        var rootId = result.Document.DocumentElement!.Value;
        return (doc, rootId);
    }

    [Fact]
    public void ApplyAppendsByDefault()
    {
        var (doc, rootId) = BuildDoc();
        var root = (XdmElement)doc.Resolve(rootId)!;
        var countBefore = root.Children.Count;

        var cmd = new InsertElementCommand(doc, rootId, "newChild");
        doc.Undo.Execute(cmd);

        var updated = (XdmElement)doc.Resolve(rootId)!;
        Assert.Equal(countBefore + 1, updated.Children.Count);
        Assert.Equal(updated.Children[^1], updated.Children[countBefore]);
    }

    [Fact]
    public void ApplyInsertsAtIndex()
    {
        var (doc, rootId) = BuildDoc();

        var cmd = new InsertElementCommand(doc, rootId, "inserted", insertIndex: 1);
        doc.Undo.Execute(cmd);

        var updated = (XdmElement)doc.Resolve(rootId)!;
        var insertedNode = doc.Resolve(updated.Children[1]) as XdmElement;
        Assert.NotNull(insertedNode);
        Assert.Equal("inserted", insertedNode.LocalName);
    }

    [Fact]
    public void ApplyAllocatesFreshNodeId()
    {
        var (doc, rootId) = BuildDoc();
        var existingIds = doc.Root.Children
            .Concat(
                ((XdmElement)doc.Resolve(doc.Root.DocumentElement!.Value)!).Children
                    .SelectMany(cid =>
                    {
                        var c = doc.Resolve(cid) as XdmElement;
                        return c?.Children ?? Enumerable.Empty<NodeId>();
                    })
            )
            .Append(doc.Root.DocumentElement!.Value)
            .ToHashSet();

        var cmd = new InsertElementCommand(doc, rootId, "fresh");
        doc.Undo.Execute(cmd);

        var updated = (XdmElement)doc.Resolve(rootId)!;
        var insertedId = updated.Children[^1];
        Assert.DoesNotContain(insertedId, existingIds);
    }

    [Fact]
    public void ApplyInsertedNodeIsResolvable()
    {
        var (doc, rootId) = BuildDoc();
        var cmd = new InsertElementCommand(doc, rootId, "resolvable");
        doc.Undo.Execute(cmd);

        var updated = (XdmElement)doc.Resolve(rootId)!;
        var insertedId = updated.Children[^1];
        var resolved = doc.Resolve(insertedId);
        Assert.NotNull(resolved);
        Assert.IsType<XdmElement>(resolved);
    }

    [Fact]
    public void ApplyInsertedNodeIsEmpty()
    {
        var (doc, rootId) = BuildDoc();
        var cmd = new InsertElementCommand(doc, rootId, "empty");
        doc.Undo.Execute(cmd);

        var updated = (XdmElement)doc.Resolve(rootId)!;
        var insertedId = updated.Children[^1];
        var inserted = (XdmElement)doc.Resolve(insertedId)!;
        Assert.Empty(inserted.Children);
        Assert.Empty(inserted.Attributes);
    }

    [Fact]
    public void RevertRestoresParentChildren()
    {
        var (doc, rootId) = BuildDoc();
        var before = (XdmElement)doc.Resolve(rootId)!;
        var originalChildren = before.Children.ToList();

        var cmd = new InsertElementCommand(doc, rootId, "willBeReverted");
        doc.Undo.Execute(cmd);
        doc.Undo.Undo();

        var after = (XdmElement)doc.Resolve(rootId)!;
        Assert.Equal(originalChildren, after.Children);
    }

    [Fact]
    public void RevertOrphanedNodeStaysInById()
    {
        var (doc, rootId) = BuildDoc();
        var cmd = new InsertElementCommand(doc, rootId, "orphaned");
        doc.Undo.Execute(cmd);

        // Capture the inserted id before revert.
        var updated = (XdmElement)doc.Resolve(rootId)!;
        var insertedId = updated.Children[^1];

        doc.Undo.Undo();

        // Node must still be resolvable (Compact-on-Save handles eviction, not Revert).
        var orphan = doc.Resolve(insertedId);
        Assert.NotNull(orphan);
    }

    [Fact]
    public void RedoReusesSameNodeId()
    {
        var (doc, rootId) = BuildDoc();
        var cmd = new InsertElementCommand(doc, rootId, "reuseMe");
        doc.Undo.Execute(cmd);

        // Capture the inserted id.
        var afterApply = (XdmElement)doc.Resolve(rootId)!;
        var firstId = afterApply.Children[^1];

        // Revert (undo).
        doc.Undo.Undo();

        // Redo.
        doc.Undo.Redo();

        // The same NodeId must be at the end of the children — no second allocation.
        var afterRedo = (XdmElement)doc.Resolve(rootId)!;
        var redoId = afterRedo.Children[^1];
        Assert.Equal(firstId, redoId);
    }

    [Fact]
    public async Task ApplyThenSerializeContainsNewElement()
    {
        var (doc, rootId) = BuildDoc();
        var cmd = new InsertElementCommand(doc, rootId, "warranty");
        doc.Undo.Execute(cmd);

        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "output.xml");
        try
        {
            await doc.SaveAsAsync(new Uri(path), TestContext.Current.CancellationToken).ConfigureAwait(true);
            var xml = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Contains("<warranty", xml, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryMergeWithDoesNotMerge()
    {
        var (doc, rootId) = BuildDoc();
        var first = new InsertElementCommand(doc, rootId, "a");
        doc.Undo.Execute(first);

        var second = new InsertElementCommand(doc, rootId, "b");
        doc.Undo.Execute(second);

        // Two inserts → two undo steps.
        Assert.True(doc.Undo.CanUndo);
        doc.Undo.Undo();
        Assert.True(doc.Undo.CanUndo);
        doc.Undo.Undo();
        Assert.False(doc.Undo.CanUndo);
    }
}
