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
/// Tests for <see cref="SetTextContentCommand"/> when applied to empty elements
/// (elements with no children). This covers the CreatedNewText path introduced in Plan 10.
/// </summary>
public class SetTextContentCommandEmptyElementTests
{
    private const string EmptyElementXml = "<root><empty/></root>";
    private const string ContainerElementXml = "<root><parent><child/></parent></root>";

    /// <summary>
    /// Builds a document from <see cref="EmptyElementXml"/> and returns the document plus
    /// the <see cref="NodeId"/> of the <c>&lt;empty/&gt;</c> element.
    /// </summary>
    private static (XmlDocument Doc, NodeId EmptyElementId) BuildEmptyDoc()
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
        var emptyId = rootElem.Children.First(id => doc.Resolve(id) is XdmElement);

        return (doc, emptyId);
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
    public void ApplyOnEmptyElementCreatesTextChild()
    {
        var (doc, emptyId) = BuildEmptyDoc();
        var cmd = new SetTextContentCommand(doc, emptyId, "hello");

        doc.Undo.Execute(cmd);

        var textChild = GetTextChild(doc, emptyId);
        Assert.NotNull(textChild);
        Assert.Equal("hello", textChild.Value);
    }

    [Fact]
    public void ApplyOnEmptyElementAllocatesFreshNodeId()
    {
        var (doc, emptyId) = BuildEmptyDoc();

        // Capture the current max node id before Apply.
        ulong maxBefore = 0;
        var element = (XdmElement)doc.Resolve(emptyId)!;
        // Walk all known ids by resolving every child of the root (approximation).
        var rootId = element.Parent!.Value;
        // We inspect the emptyId value itself as a proxy for the pre-existing max.
        maxBefore = emptyId.Value;

        var cmd = new SetTextContentCommand(doc, emptyId, "hello");
        doc.Undo.Execute(cmd);

        var textChild = GetTextChild(doc, emptyId);
        Assert.NotNull(textChild);
        Assert.True(textChild.Id.Value > maxBefore,
            $"Expected new text node id ({textChild.Id.Value}) > pre-existing max ({maxBefore}).");
    }

    [Fact]
    public void ApplyOnEmptyElementChildrenContainsTextNodeId()
    {
        var (doc, emptyId) = BuildEmptyDoc();
        var cmd = new SetTextContentCommand(doc, emptyId, "hello");

        doc.Undo.Execute(cmd);

        var updatedElement = (XdmElement)doc.Resolve(emptyId)!;
        Assert.Single(updatedElement.Children);

        var textChild = GetTextChild(doc, emptyId);
        Assert.NotNull(textChild);
        Assert.Equal(updatedElement.Children[0], textChild.Id);
    }

    [Fact]
    public void RevertOnEmptyElementRestoresEmptyChildren()
    {
        var (doc, emptyId) = BuildEmptyDoc();
        var cmd = new SetTextContentCommand(doc, emptyId, "hello");

        doc.Undo.Execute(cmd);
        doc.Undo.Undo();

        var restoredElement = (XdmElement)doc.Resolve(emptyId)!;
        Assert.Empty(restoredElement.Children);
    }

    [Fact]
    public void RedoOnEmptyElementReusesSameTextId()
    {
        var (doc, emptyId) = BuildEmptyDoc();
        var cmd = new SetTextContentCommand(doc, emptyId, "hello");

        // Apply
        doc.Undo.Execute(cmd);
        var textChildAfterApply = GetTextChild(doc, emptyId);
        Assert.NotNull(textChildAfterApply);
        var originalTextId = textChildAfterApply.Id;

        // Revert (undo)
        doc.Undo.Undo();
        var restoredElement = (XdmElement)doc.Resolve(emptyId)!;
        Assert.Empty(restoredElement.Children);

        // Redo
        doc.Undo.Redo();
        var textChildAfterRedo = GetTextChild(doc, emptyId);
        Assert.NotNull(textChildAfterRedo);

        // Same NodeId must be reused.
        Assert.Equal(originalTextId, textChildAfterRedo.Id);
    }

    [Fact]
    public void ApplyOnEmptyElementWithEmptyStringValue()
    {
        var (doc, emptyId) = BuildEmptyDoc();
        var cmd = new SetTextContentCommand(doc, emptyId, "");

        doc.Undo.Execute(cmd);

        // Empty string text node must exist.
        var updatedElement = (XdmElement)doc.Resolve(emptyId)!;
        Assert.Single(updatedElement.Children);

        var textChild = GetTextChild(doc, emptyId);
        Assert.NotNull(textChild);
        Assert.Equal("", textChild.Value);
    }

    [Fact]
    public async Task ApplyOnEmptyElementThenSerialize()
    {
        var (doc, emptyId) = BuildEmptyDoc();
        var cmd = new SetTextContentCommand(doc, emptyId, "hello");
        doc.Undo.Execute(cmd);

        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "output.xml");
        try
        {
            await doc.SaveAsAsync(new Uri(path), TestContext.Current.CancellationToken).ConfigureAwait(true);

            var xml = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Contains("<empty>hello</empty>", xml, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ApplyOnContainerElementStillThrows()
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
        var parentId = rootElem.Children.First(id => doc.Resolve(id) is XdmElement);

        var cmd = new SetTextContentCommand(doc, parentId, "some text");

        var ex = Assert.Throws<InvalidOperationException>(() => cmd.Apply(doc));
        Assert.Contains("element children", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferencedNodeIdsIncludesNewTextIdAfterApply()
    {
        var (doc, emptyId) = BuildEmptyDoc();
        var cmd = new SetTextContentCommand(doc, emptyId, "hello");

        // Before Apply, ReferencedNodeIds should be empty.
        Assert.Empty(cmd.ReferencedNodeIds);

        doc.Undo.Execute(cmd);

        // After Apply, ReferencedNodeIds must contain the new text node's id.
        var textChild = GetTextChild(doc, emptyId);
        Assert.NotNull(textChild);
        Assert.Contains(textChild.Id, cmd.ReferencedNodeIds);
    }
}
