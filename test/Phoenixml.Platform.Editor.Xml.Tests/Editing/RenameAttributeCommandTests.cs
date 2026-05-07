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

/// <summary>
/// Tests for <see cref="RenameAttributeCommand"/>.
/// </summary>
public class RenameAttributeCommandTests
{
    private const string CatalogXml =
        "<catalog>" +
        "<product id=\"p1\" featured=\"true\"><name>Widget</name></product>" +
        "<product id=\"p2\"><name>Gadget</name></product>" +
        "</catalog>";

    /// <summary>
    /// Builds a test document and returns the document, the root element id,
    /// and the NodeId of the first attribute on the first product element.
    /// </summary>
    private static (XmlDocument Doc, NodeId RootId, NodeId AttrId) BuildDoc()
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

        // Get the first product element
        var productId = rootElem.Children.First(id => doc.Resolve(id) is XdmElement);
        var productElem = (XdmElement)doc.Resolve(productId)!;

        // Get the first attribute on the product element (id="p1")
        var attrId = productElem.Attributes[0];

        return (doc, rootId, attrId);
    }

    [Fact]
    public void ApplyRenamesAttributeInPlace()
    {
        var (doc, _, attrId) = BuildDoc();
        var cmd = new RenameAttributeCommand(doc, attrId, "productId");

        doc.Undo.Execute(cmd);

        var renamed = Assert.IsType<XdmAttribute>(doc.Resolve(attrId));
        Assert.Equal("productId", renamed.LocalName);
    }

    [Fact]
    public void RevertRestoresOriginalName()
    {
        var (doc, _, attrId) = BuildDoc();
        var cmd = new RenameAttributeCommand(doc, attrId, "productId");

        doc.Undo.Execute(cmd);
        doc.Undo.Undo();

        var restored = Assert.IsType<XdmAttribute>(doc.Resolve(attrId));
        Assert.Equal("id", restored.LocalName);
    }

    [Fact]
    public void ApplyKeepsNodeIdAndValue()
    {
        var (doc, _, attrId) = BuildDoc();

        var before = Assert.IsType<XdmAttribute>(doc.Resolve(attrId));
        var originalValue = before.Value;

        var cmd = new RenameAttributeCommand(doc, attrId, "productId");
        doc.Undo.Execute(cmd);

        var after = Assert.IsType<XdmAttribute>(doc.Resolve(attrId));
        Assert.Equal(attrId, after.Id);
        Assert.Equal(originalValue, after.Value);
    }

    [Fact]
    public void TryMergeWithTwoConsecutiveRenamesOnSameAttributeMerge()
    {
        // Execute two renames on the same attribute via UndoStack so TryMergeWith is invoked.
        // UndoStack merges consecutive commands on the same attribute into one undo step.
        var (doc, _, attrId) = BuildDoc();

        var first = new RenameAttributeCommand(doc, attrId, "foo");
        doc.Undo.Execute(first);

        var second = new RenameAttributeCommand(doc, attrId, "bar");
        doc.Undo.Execute(second);

        // After two executes, the undo stack should have ONE merged step.
        // Undoing once should restore "id" (not "foo").
        Assert.True(doc.Undo.CanUndo);
        doc.Undo.Undo();

        var reverted = Assert.IsType<XdmAttribute>(doc.Resolve(attrId));
        Assert.Equal("id", reverted.LocalName);

        // Confirm there is now nothing left to undo (only one merged step was on the stack).
        Assert.False(doc.Undo.CanUndo);
    }

    [Fact]
    public async Task ApplyThenSerializeProducesNewName()
    {
        var (doc, _, attrId) = BuildDoc();
        var cmd = new RenameAttributeCommand(doc, attrId, "productId");
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

            // Drill to first product element
            var productId = reloadedRoot.Children.First(id => reloaded.Resolve(id) is XdmElement);
            var productElem = Assert.IsType<XdmElement>(reloaded.Resolve(productId));

            // Find an attribute named "productId" (the renamed one)
            var renamedAttrId = productElem.Attributes.First(id =>
                reloaded.Resolve(id) is XdmAttribute a && a.LocalName == "productId");
            var renamedAttr = Assert.IsType<XdmAttribute>(reloaded.Resolve(renamedAttrId));
            Assert.Equal("productId", renamedAttr.LocalName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
