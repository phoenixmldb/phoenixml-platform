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
/// Tests for <see cref="SetAttributeValueCommand"/>.
/// </summary>
public class SetAttributeValueCommandTests
{
    private const string CatalogXml =
        "<catalog>" +
        "<product id=\"E001\" featured=\"true\"><name>Laptop Pro</name></product>" +
        "<product id=\"E002\"><name>Gadget</name></product>" +
        "</catalog>";

    /// <summary>
    /// Builds a test document and returns the document, the first product element id,
    /// and the NodeId of the first attribute on the first product element (id="E001").
    /// </summary>
    private static (XmlDocument Doc, NodeId ProductId, NodeId AttrId) BuildDoc()
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

        // Get the first attribute on the product element (id="E001")
        var attrId = productElem.Attributes[0];

        return (doc, productId, attrId);
    }

    [Fact]
    public void ApplySetsAttributeValue()
    {
        var (doc, _, attrId) = BuildDoc();
        var cmd = new SetAttributeValueCommand(doc, attrId, "X999");

        doc.Undo.Execute(cmd);

        var updated = Assert.IsType<XdmAttribute>(doc.Resolve(attrId));
        Assert.Equal("X999", updated.Value);
    }

    [Fact]
    public void RevertRestoresOriginalValue()
    {
        var (doc, _, attrId) = BuildDoc();
        var cmd = new SetAttributeValueCommand(doc, attrId, "X999");

        doc.Undo.Execute(cmd);
        doc.Undo.Undo();

        var restored = Assert.IsType<XdmAttribute>(doc.Resolve(attrId));
        Assert.Equal("E001", restored.Value);
    }

    [Fact]
    public void ApplyKeepsLocalNameAndPrefix()
    {
        var (doc, _, attrId) = BuildDoc();
        var before = Assert.IsType<XdmAttribute>(doc.Resolve(attrId));
        var originalLocalName = before.LocalName;
        var originalPrefix = before.Prefix;
        var originalNamespace = before.Namespace;

        var cmd = new SetAttributeValueCommand(doc, attrId, "X999");
        doc.Undo.Execute(cmd);

        var after = Assert.IsType<XdmAttribute>(doc.Resolve(attrId));
        Assert.Equal(originalLocalName, after.LocalName);
        Assert.Equal(originalPrefix, after.Prefix);
        Assert.Equal(originalNamespace, after.Namespace);
    }

    [Fact]
    public void ApplyKeepsNodeId()
    {
        var (doc, _, attrId) = BuildDoc();
        var cmd = new SetAttributeValueCommand(doc, attrId, "X999");

        doc.Undo.Execute(cmd);

        var after = Assert.IsType<XdmAttribute>(doc.Resolve(attrId));
        Assert.Equal(attrId, after.Id);
    }

    [Fact]
    public void TryMergeWithTwoConsecutiveSetsOnSameAttributeMerge()
    {
        // Execute two value sets on the same attribute via UndoStack so TryMergeWith is invoked.
        // UndoStack merges consecutive commands on the same attribute into one undo step.
        var (doc, _, attrId) = BuildDoc();

        var first = new SetAttributeValueCommand(doc, attrId, "interim");
        doc.Undo.Execute(first);

        var second = new SetAttributeValueCommand(doc, attrId, "X999");
        doc.Undo.Execute(second);

        // After two executes, the undo stack should have ONE merged step.
        // Undoing once should restore "E001" (not "interim").
        Assert.True(doc.Undo.CanUndo);
        doc.Undo.Undo();

        var reverted = Assert.IsType<XdmAttribute>(doc.Resolve(attrId));
        Assert.Equal("E001", reverted.Value);

        // Confirm there is now nothing left to undo (only one merged step was on the stack).
        Assert.False(doc.Undo.CanUndo);
    }

    [Fact]
    public void TryMergeWithDifferentAttributesDoNotMerge()
    {
        var (doc, productId, attrId) = BuildDoc();
        var productElem = Assert.IsType<XdmElement>(doc.Resolve(productId));

        // Use the second attribute (featured="true") as a different target
        var secondAttrId = productElem.Attributes[1];

        var first = new SetAttributeValueCommand(doc, attrId, "X999");
        doc.Undo.Execute(first);

        var second = new SetAttributeValueCommand(doc, secondAttrId, "false");
        doc.Undo.Execute(second);

        // Two different attributes — should NOT merge; there should be two undo steps.
        Assert.True(doc.Undo.CanUndo);
        doc.Undo.Undo();

        // After one undo, the second attribute is restored but the first is still changed.
        var firstAttr = Assert.IsType<XdmAttribute>(doc.Resolve(attrId));
        Assert.Equal("X999", firstAttr.Value);  // still changed

        Assert.True(doc.Undo.CanUndo);
        doc.Undo.Undo();

        // After second undo, the first attribute is restored.
        var firstAttrRestored = Assert.IsType<XdmAttribute>(doc.Resolve(attrId));
        Assert.Equal("E001", firstAttrRestored.Value);
    }

    [Fact]
    public async Task ApplyThenSerializeProducesNewValue()
    {
        var (doc, _, attrId) = BuildDoc();
        var cmd = new SetAttributeValueCommand(doc, attrId, "X999");
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

            // Find the id attribute and verify it has the new value
            var idAttrId = productElem.Attributes.First(id =>
                reloaded.Resolve(id) is XdmAttribute a && a.LocalName == "id");
            var idAttr = Assert.IsType<XdmAttribute>(reloaded.Resolve(idAttrId));
            Assert.Equal("X999", idAttr.Value);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ApplyWithEmptyStringWorks()
    {
        var (doc, _, attrId) = BuildDoc();
        var cmd = new SetAttributeValueCommand(doc, attrId, "");

        doc.Undo.Execute(cmd);

        var updated = Assert.IsType<XdmAttribute>(doc.Resolve(attrId));
        Assert.Equal("", updated.Value);
    }
}
