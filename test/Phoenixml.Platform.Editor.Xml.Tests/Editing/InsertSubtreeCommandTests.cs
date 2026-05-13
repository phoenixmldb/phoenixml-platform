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

public class InsertSubtreeCommandTests
{
    // <library>
    //   <book isbn="978-1">
    //     <title>Existing Book</title>
    //   </book>
    // </library>
    private const string LibraryXml =
        "<library>" +
        "<book isbn=\"978-1\"><title>Existing Book</title></book>" +
        "</library>";

    private static (XmlDocument Doc, NodeId LibraryId) BuildDoc()
    {
        var (result, nsTable) = XmlDocument.ParseXml(LibraryXml);
        var doc = new XmlDocument(
            PlatformDocumentId.New(),
            source: null,
            "library.xml",
            result,
            nsTable);
        var libraryId = result.Document.DocumentElement!.Value;
        return (doc, libraryId);
    }

    /// <summary>Returns the maximum NodeId value currently in use by walking all nodes in the
    /// document root's reachable subtree.</summary>
    private static ulong MaxNodeIdValue(XmlDocument doc)
    {
        ulong max = 0;
        void Walk(XdmNode node)
        {
            if (node.Id.Value > max) max = node.Id.Value;
            if (node is not XdmElement el) return;
            foreach (var aid in el.Attributes)
                if (doc.Resolve(aid) is XdmNode a) Walk(a);
            foreach (var cid in el.Children)
                if (doc.Resolve(cid) is XdmNode c) Walk(c);
        }
        Walk(doc.Root);
        foreach (var cid in doc.Root.Children)
            if (doc.Resolve(cid) is XdmNode child) Walk(child);
        return max;
    }

    [Fact]
    public void InsertedSubtreeRootHasCorrectParentPointer()
    {
        // Regression for v0.11 smoke-test bug: a pasted subtree's root had Parent = null
        // (from the parser's synthetic doc), so DeleteNodeCommand couldn't delete it
        // because it walks Parent to find the parent element.
        var (doc, libraryId) = BuildDoc();
        var cmd = new InsertSubtreeCommand(doc, libraryId, "<note>hi</note>");
        doc.Undo.Execute(cmd);

        var libraryAfter = (XdmElement)doc.Resolve(libraryId)!;
        var pastedId = libraryAfter.Children[^1];
        var pastedRoot = doc.Resolve(pastedId) as XdmElement;
        Assert.NotNull(pastedRoot);
        Assert.Equal(libraryId, pastedRoot.Parent);

        // The pasted subtree must be deletable via DeleteNodeCommand — which walks
        // Parent to find its container. Pre-fix this threw InvalidOperationException
        // because Parent was the parser's synthetic doc, not an XdmElement.
        doc.Undo.Execute(new DeleteNodeCommand(doc, pastedId));
        var libraryFinal = (XdmElement)doc.Resolve(libraryId)!;
        Assert.DoesNotContain(pastedId, libraryFinal.Children);
    }

    [Fact]
    public void ApplyAppendsSubtreeAsLastChild()
    {
        var (doc, libraryId) = BuildDoc();
        var libraryBefore = (XdmElement)doc.Resolve(libraryId)!;
        var countBefore = libraryBefore.Children.Count;

        var cmd = new InsertSubtreeCommand(doc, libraryId, "<title>New Title</title>");
        doc.Undo.Execute(cmd);

        var libraryAfter = (XdmElement)doc.Resolve(libraryId)!;
        Assert.Equal(countBefore + 1, libraryAfter.Children.Count);

        var lastId = libraryAfter.Children[^1];
        var lastElem = doc.Resolve(lastId) as XdmElement;
        Assert.NotNull(lastElem);
        Assert.Equal("title", lastElem.LocalName);
    }

    [Fact]
    public void ApplyAllocatesFreshNodeIds()
    {
        var (doc, libraryId) = BuildDoc();
        var maxBefore = MaxNodeIdValue(doc);

        var cmd = new InsertSubtreeCommand(doc, libraryId, "<extra/>");
        doc.Undo.Execute(cmd);

        var libraryAfter = (XdmElement)doc.Resolve(libraryId)!;
        var insertedId = libraryAfter.Children[^1];
        Assert.True(insertedId.Value > maxBefore,
            $"Inserted NodeId {insertedId.Value} must be greater than max pre-existing id {maxBefore}.");
    }

    [Fact]
    public void ApplyParsesAttributesAndDescendants()
    {
        var (doc, libraryId) = BuildDoc();

        var cmd = new InsertSubtreeCommand(
            doc, libraryId,
            "<book isbn=\"978-2\"><title>Pasted Book</title></book>");
        doc.Undo.Execute(cmd);

        var library = (XdmElement)doc.Resolve(libraryId)!;
        var insertedId = library.Children[^1];
        var book = doc.Resolve(insertedId) as XdmElement;
        Assert.NotNull(book);
        Assert.Equal("book", book.LocalName);

        // Attribute must be present.
        Assert.NotEmpty(book.Attributes);
        var attrId = book.Attributes[0];
        var attr = doc.Resolve(attrId) as XdmAttribute;
        Assert.NotNull(attr);
        Assert.Equal("isbn", attr.LocalName);
        Assert.Equal("978-2", attr.Value);

        // Child <title> must be present.
        Assert.NotEmpty(book.Children);
        var titleId = book.Children[0];
        var title = doc.Resolve(titleId) as XdmElement;
        Assert.NotNull(title);
        Assert.Equal("title", title.LocalName);
    }

    [Fact]
    public void RevertRestoresParentChildren()
    {
        var (doc, libraryId) = BuildDoc();
        var originalChildren = ((XdmElement)doc.Resolve(libraryId)!).Children.ToList();

        var cmd = new InsertSubtreeCommand(doc, libraryId, "<extra/>");
        doc.Undo.Execute(cmd);
        doc.Undo.Undo();

        var restored = (XdmElement)doc.Resolve(libraryId)!;
        Assert.Equal(originalChildren, restored.Children);
    }

    [Fact]
    public void RevertOrphansSubtreeButLeavesInById()
    {
        var (doc, libraryId) = BuildDoc();

        var cmd = new InsertSubtreeCommand(doc, libraryId, "<orphan/>");
        doc.Undo.Execute(cmd);

        // Capture the inserted id before revert.
        var insertedId = ((XdmElement)doc.Resolve(libraryId)!).Children[^1];

        doc.Undo.Undo();

        // Node must still be resolvable after revert.
        var orphan = doc.Resolve(insertedId);
        Assert.NotNull(orphan);
    }

    [Fact]
    public void RedoReusesSameNodeIds()
    {
        var (doc, libraryId) = BuildDoc();

        var cmd = new InsertSubtreeCommand(doc, libraryId, "<reuseMe/>");
        doc.Undo.Execute(cmd);

        var afterApply = (XdmElement)doc.Resolve(libraryId)!;
        var firstId = afterApply.Children[^1];

        doc.Undo.Undo();
        doc.Undo.Redo();

        var afterRedo = (XdmElement)doc.Resolve(libraryId)!;
        var redoId = afterRedo.Children[^1];

        Assert.Equal(firstId, redoId);
    }

    [Fact]
    public void ReferencedNodeIdsIncludesEntireSubtree()
    {
        var (doc, libraryId) = BuildDoc();

        var cmd = new InsertSubtreeCommand(
            doc, libraryId,
            "<book isbn=\"978-3\"><title>Ref Test</title></book>");
        doc.Undo.Execute(cmd);

        var referenced = cmd.ReferencedNodeIds.ToHashSet();

        // The inserted <book> root.
        var library = (XdmElement)doc.Resolve(libraryId)!;
        var bookId = library.Children[^1];
        Assert.Contains(bookId, referenced);

        // The <book>'s attribute.
        var book = (XdmElement)doc.Resolve(bookId)!;
        foreach (var attrId in book.Attributes)
            Assert.Contains(attrId, referenced);

        // The <title> child.
        foreach (var childId in book.Children)
            Assert.Contains(childId, referenced);
    }

    [Fact]
    public void ApplyMalformedXmlThrows()
    {
        var (doc, libraryId) = BuildDoc();
        var childrenBefore = ((XdmElement)doc.Resolve(libraryId)!).Children.ToList();

        var cmd = new InsertSubtreeCommand(doc, libraryId, "<unclosed");

        Assert.Throws<System.Xml.XmlException>(() => doc.Undo.Execute(cmd));

        // Parent children must be unchanged after failure.
        var childrenAfter = ((XdmElement)doc.Resolve(libraryId)!).Children.ToList();
        Assert.Equal(childrenBefore, childrenAfter);
    }

    [Fact]
    public void ApplyOnNonElementParentThrows()
    {
        var (doc, libraryId) = BuildDoc();

        // Grab the doc-node id (not an element).
        var docNodeId = doc.Root.Id;

        var cmd = new InsertSubtreeCommand(doc, docNodeId, "<extra/>");
        Assert.Throws<InvalidOperationException>(() => doc.Undo.Execute(cmd));
    }

    [Fact]
    public async Task ApplyThenSerializeRoundTrip()
    {
        var (doc, libraryId) = BuildDoc();

        var cmd = new InsertSubtreeCommand(
            doc, libraryId,
            "<book isbn=\"978-rt\"><title>Round Trip</title></book>");
        doc.Undo.Execute(cmd);

        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
        System.IO.Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "output.xml");
        try
        {
            await doc.SaveAsAsync(new Uri(path), TestContext.Current.CancellationToken).ConfigureAwait(true);
            var xml = await System.IO.File.ReadAllTextAsync(
                path, TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Contains("978-rt", xml, StringComparison.Ordinal);
            Assert.Contains("Round Trip", xml, StringComparison.Ordinal);
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }
}
