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
/// Tests for <see cref="SetTextContentCommand"/>.
/// </summary>
public class SetTextContentCommandTests
{
    private const string CatalogXml =
        "<catalog>" +
        "<product id=\"E001\"><name>Laptop Pro</name></product>" +
        "<product id=\"E002\"><name>Gadget</name></product>" +
        "</catalog>";

    private const string EmptyElementXml = "<root><empty/></root>";
    private const string ContainerElementXml = "<root><parent><child/></parent></root>";

    /// <summary>
    /// Builds a test document and returns the document plus the NodeId of
    /// the first &lt;name&gt; element (which contains "Laptop Pro").
    /// </summary>
    private static (XmlDocument Doc, NodeId NameElementId) BuildDoc()
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

        // Get the <name> element child
        var nameId = productElem.Children.First(id =>
            doc.Resolve(id) is XdmElement e && e.LocalName == "name");

        return (doc, nameId);
    }

    /// <summary>
    /// Gets the single XdmText child of an element, or null if none exists.
    /// </summary>
    private static XdmText? GetTextChild(XmlDocument doc, NodeId elementId)
    {
        var element = (XdmElement)doc.Resolve(elementId)!;
        foreach (var childId in element.Children)
        {
            if (doc.Resolve(childId) is XdmText t)
                return t;
        }
        return null;
    }

    [Fact]
    public void ApplySetsTextValue()
    {
        var (doc, nameId) = BuildDoc();
        var cmd = new SetTextContentCommand(doc, nameId, "Laptop Pro 2");

        doc.Undo.Execute(cmd);

        var textChild = GetTextChild(doc, nameId);
        Assert.NotNull(textChild);
        Assert.Equal("Laptop Pro 2", textChild.Value);
    }

    [Fact]
    public void RevertRestoresOriginalText()
    {
        var (doc, nameId) = BuildDoc();
        var cmd = new SetTextContentCommand(doc, nameId, "Laptop Pro 2");

        doc.Undo.Execute(cmd);
        doc.Undo.Undo();

        var textChild = GetTextChild(doc, nameId);
        Assert.NotNull(textChild);
        Assert.Equal("Laptop Pro", textChild.Value);
    }

    [Fact]
    public void ApplyKeepsElementUnchanged()
    {
        var (doc, nameId) = BuildDoc();

        var before = Assert.IsType<XdmElement>(doc.Resolve(nameId));
        var originalChildren = before.Children.ToList();
        var originalAttributes = before.Attributes.ToList();

        var cmd = new SetTextContentCommand(doc, nameId, "Laptop Pro 2");
        doc.Undo.Execute(cmd);

        var after = Assert.IsType<XdmElement>(doc.Resolve(nameId));
        // Children list must reference the same NodeIds (element not mutated)
        Assert.Equal(originalChildren, after.Children);
        Assert.Equal(originalAttributes, after.Attributes);
    }

    [Fact]
    public void ApplyOnEmptyElementThrows()
    {
        var (result, nsTable) = XmlDocument.ParseXml(EmptyElementXml);
        var doc = new XmlDocument(
            PlatformDocumentId.New(),
            source: null,
            "empty.xml",
            result,
            nsTable);

        var rootId = result.Document.DocumentElement!.Value;
        var rootElem = (XdmElement)doc.Resolve(rootId)!;

        // Find the <empty/> element
        var emptyId = rootElem.Children.First(id => doc.Resolve(id) is XdmElement);

        var cmd = new SetTextContentCommand(doc, emptyId, "some text");

        var ex = Assert.Throws<InvalidOperationException>(() => cmd.Apply(doc));
        Assert.Contains("no text child", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyOnContainerElementThrows()
    {
        var (result, nsTable) = XmlDocument.ParseXml(ContainerElementXml);
        var doc = new XmlDocument(
            PlatformDocumentId.New(),
            source: null,
            "container.xml",
            result,
            nsTable);

        var rootId = result.Document.DocumentElement!.Value;
        var rootElem = (XdmElement)doc.Resolve(rootId)!;

        // Find the <parent> element (which has element children)
        var parentId = rootElem.Children.First(id => doc.Resolve(id) is XdmElement);

        var cmd = new SetTextContentCommand(doc, parentId, "some text");

        var ex = Assert.Throws<InvalidOperationException>(() => cmd.Apply(doc));
        Assert.Contains("element children", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryMergeWithTwoConsecutiveSetsOnSameElementMerge()
    {
        // Execute two text sets on the same element via UndoStack so TryMergeWith is invoked.
        var (doc, nameId) = BuildDoc();

        var first = new SetTextContentCommand(doc, nameId, "Laptop Pro Interim");
        doc.Undo.Execute(first);

        var second = new SetTextContentCommand(doc, nameId, "Laptop Pro 2");
        doc.Undo.Execute(second);

        // After two executes, the undo stack should have ONE merged step.
        // Undoing once should restore "Laptop Pro" (not "Laptop Pro Interim").
        Assert.True(doc.Undo.CanUndo);
        doc.Undo.Undo();

        var textChild = GetTextChild(doc, nameId);
        Assert.NotNull(textChild);
        Assert.Equal("Laptop Pro", textChild.Value);

        // Confirm there is now nothing left to undo (only one merged step was on the stack).
        Assert.False(doc.Undo.CanUndo);
    }

    [Fact]
    public void TryMergeWithDifferentElementsDoNotMerge()
    {
        var (doc, nameId) = BuildDoc();

        // Navigate up to root, then get the second product's <name> element
        var nameElem = (XdmElement)doc.Resolve(nameId)!;
        var productId = nameElem.Parent!.Value;
        var productElem = (XdmElement)doc.Resolve(productId)!;
        var rootId = productElem.Parent!.Value;
        var rootElem = (XdmElement)doc.Resolve(rootId)!;

        var products = rootElem.Children.Where(id => doc.Resolve(id) is XdmElement).ToList();
        var secondProductElem = (XdmElement)doc.Resolve(products[1])!;
        var secondNameId = secondProductElem.Children.First(id =>
            doc.Resolve(id) is XdmElement e && e.LocalName == "name");

        var first = new SetTextContentCommand(doc, nameId, "Laptop Pro 2");
        doc.Undo.Execute(first);

        var second = new SetTextContentCommand(doc, secondNameId, "Gadget 2");
        doc.Undo.Execute(second);

        // Two different elements — should NOT merge; there should be two undo steps.
        Assert.True(doc.Undo.CanUndo);
        doc.Undo.Undo();

        // After one undo, the second element's text is restored but the first is still changed.
        var secondTextChild = GetTextChild(doc, secondNameId);
        Assert.NotNull(secondTextChild);
        Assert.Equal("Gadget", secondTextChild.Value);  // restored

        var firstTextChild = GetTextChild(doc, nameId);
        Assert.NotNull(firstTextChild);
        Assert.Equal("Laptop Pro 2", firstTextChild.Value);  // still changed

        Assert.True(doc.Undo.CanUndo);
        doc.Undo.Undo();

        // After second undo, the first element's text is also restored.
        var firstTextChildRestored = GetTextChild(doc, nameId);
        Assert.NotNull(firstTextChildRestored);
        Assert.Equal("Laptop Pro", firstTextChildRestored.Value);
    }

    [Fact]
    public async Task ApplyThenSerializeProducesNewText()
    {
        var (doc, nameId) = BuildDoc();
        var cmd = new SetTextContentCommand(doc, nameId, "Laptop Pro 2");
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

            // Find the <name> element
            var reloadedNameId = productElem.Children.First(id =>
                reloaded.Resolve(id) is XdmElement e && e.LocalName == "name");

            // Verify text content
            var reloadedTextChild = GetTextChild(reloaded, reloadedNameId);
            Assert.NotNull(reloadedTextChild);
            Assert.Equal("Laptop Pro 2", reloadedTextChild.Value);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyWithEmptyStringWorks()
    {
        var (doc, nameId) = BuildDoc();
        var cmd = new SetTextContentCommand(doc, nameId, "");

        doc.Undo.Execute(cmd);

        var textChild = GetTextChild(doc, nameId);
        Assert.NotNull(textChild);
        Assert.Equal("", textChild.Value);

        // Verify serialization round-trip produces well-formed XML
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "output.xml");
        try
        {
            await doc.SaveAsAsync(new Uri(path), TestContext.Current.CancellationToken).ConfigureAwait(true);
            // Just verify it serializes without throwing (well-formed XML)
            var reloaded = await XmlDocument.LoadAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.NotNull(reloaded);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
