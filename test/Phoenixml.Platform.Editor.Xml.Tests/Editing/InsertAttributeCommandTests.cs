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

public class InsertAttributeCommandTests
{
    private const string CatalogXml =
        "<catalog>" +
        "<product id=\"p1\"><name>Widget</name></product>" +
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
        var productId = rootElem.Children.FirstOrDefault(id => doc.Resolve(id) is XdmElement);
        return (doc, rootId, productId);
    }

    [Fact]
    public void ApplyAppendsAttribute()
    {
        var (doc, _, productId) = BuildDoc();
        var before = (XdmElement)doc.Resolve(productId)!;
        var attrCountBefore = before.Attributes.Count;

        var cmd = new InsertAttributeCommand(doc, productId, "sku", "W-001");
        doc.Undo.Execute(cmd);

        var after = (XdmElement)doc.Resolve(productId)!;
        Assert.Equal(attrCountBefore + 1, after.Attributes.Count);
    }

    [Fact]
    public void ApplyAllocatesFreshNodeId()
    {
        var (doc, _, productId) = BuildDoc();
        var before = (XdmElement)doc.Resolve(productId)!;
        var existingAttrIds = before.Attributes.ToHashSet();

        var cmd = new InsertAttributeCommand(doc, productId, "sku", "W-001");
        doc.Undo.Execute(cmd);

        var after = (XdmElement)doc.Resolve(productId)!;
        var newAttrId = after.Attributes[^1];
        Assert.DoesNotContain(newAttrId, existingAttrIds);
    }

    [Fact]
    public void ApplyInsertedAttrIsResolvable()
    {
        var (doc, _, productId) = BuildDoc();
        var cmd = new InsertAttributeCommand(doc, productId, "sku", "W-001");
        doc.Undo.Execute(cmd);

        var after = (XdmElement)doc.Resolve(productId)!;
        var newAttrId = after.Attributes[^1];
        var resolved = doc.Resolve(newAttrId);
        Assert.NotNull(resolved);
        Assert.IsType<XdmAttribute>(resolved);
    }

    [Fact]
    public void ApplyInsertedAttrHasGivenValue()
    {
        var (doc, _, productId) = BuildDoc();
        var cmd = new InsertAttributeCommand(doc, productId, "sku", "W-001");
        doc.Undo.Execute(cmd);

        var after = (XdmElement)doc.Resolve(productId)!;
        var newAttr = (XdmAttribute)doc.Resolve(after.Attributes[^1])!;
        Assert.Equal("sku", newAttr.LocalName);
        Assert.Equal("W-001", newAttr.Value);
    }

    [Fact]
    public void RevertRestoresParentAttributes()
    {
        var (doc, _, productId) = BuildDoc();
        var before = (XdmElement)doc.Resolve(productId)!;
        var originalAttrs = before.Attributes.ToList();

        var cmd = new InsertAttributeCommand(doc, productId, "willBeReverted", "x");
        doc.Undo.Execute(cmd);
        doc.Undo.Undo();

        var after = (XdmElement)doc.Resolve(productId)!;
        Assert.Equal(originalAttrs, after.Attributes);
    }

    [Fact]
    public void RevertOrphanedAttrStaysInById()
    {
        var (doc, _, productId) = BuildDoc();
        var cmd = new InsertAttributeCommand(doc, productId, "orphaned", "val");
        doc.Undo.Execute(cmd);

        var updated = (XdmElement)doc.Resolve(productId)!;
        var insertedId = updated.Attributes[^1];

        doc.Undo.Undo();

        // Attribute must still be resolvable after revert.
        var orphan = doc.Resolve(insertedId);
        Assert.NotNull(orphan);
    }

    [Fact]
    public void RedoReusesSameNodeId()
    {
        var (doc, _, productId) = BuildDoc();
        var cmd = new InsertAttributeCommand(doc, productId, "reuseMe", "v");
        doc.Undo.Execute(cmd);

        var afterApply = (XdmElement)doc.Resolve(productId)!;
        var firstId = afterApply.Attributes[^1];

        doc.Undo.Undo();
        doc.Undo.Redo();

        var afterRedo = (XdmElement)doc.Resolve(productId)!;
        var redoId = afterRedo.Attributes[^1];
        Assert.Equal(firstId, redoId);
    }

    [Fact]
    public async Task ApplyThenSerializeContainsNewAttribute()
    {
        var (doc, _, productId) = BuildDoc();
        var cmd = new InsertAttributeCommand(doc, productId, "sku", "W-001");
        doc.Undo.Execute(cmd);

        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "output.xml");
        try
        {
            await doc.SaveAsAsync(new Uri(path), TestContext.Current.CancellationToken).ConfigureAwait(true);
            var xml = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Contains("sku=\"W-001\"", xml, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ApplyDuplicateLocalNameStillWorks()
    {
        // XML allows duplicate local names if namespaces differ.
        // The editor does not enforce uniqueness — that is the schema validator's job.
        var (doc, _, productId) = BuildDoc();
        var before = (XdmElement)doc.Resolve(productId)!;
        var attrCountBefore = before.Attributes.Count;

        // Insert "id" twice (the element already has id="p1").
        var first = new InsertAttributeCommand(doc, productId, "id", "extra1");
        doc.Undo.Execute(first);
        var second = new InsertAttributeCommand(doc, productId, "id", "extra2");
        doc.Undo.Execute(second);

        var after = (XdmElement)doc.Resolve(productId)!;
        Assert.Equal(attrCountBefore + 2, after.Attributes.Count);
    }

    [Fact]
    public void TryMergeWithDoesNotMerge()
    {
        var (doc, _, productId) = BuildDoc();
        var first = new InsertAttributeCommand(doc, productId, "a", "1");
        doc.Undo.Execute(first);

        var second = new InsertAttributeCommand(doc, productId, "b", "2");
        doc.Undo.Execute(second);

        // Two inserts → two undo steps.
        Assert.True(doc.Undo.CanUndo);
        doc.Undo.Undo();
        Assert.True(doc.Undo.CanUndo);
        doc.Undo.Undo();
        Assert.False(doc.Undo.CanUndo);
    }
}
