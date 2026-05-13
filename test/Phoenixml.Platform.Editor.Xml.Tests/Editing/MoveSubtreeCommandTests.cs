using System;
using System.Linq;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Editing;
using Xunit;
using PlatformDocumentId = Phoenixml.Platform.Abstractions.Documents.DocumentId;

namespace Phoenixml.Platform.Editor.Xml.Tests.Editing;

public class MoveSubtreeCommandTests
{
    // <library>
    //   <book isbn="978-1"><title>Book One</title></book>
    //   <book isbn="978-2"><title>Book Two</title></book>
    //   <book isbn="978-3"><title>Book Three</title></book>
    // </library>
    private const string LibraryXml =
        "<library>" +
        "<book isbn=\"978-1\"><title>Book One</title></book>" +
        "<book isbn=\"978-2\"><title>Book Two</title></book>" +
        "<book isbn=\"978-3\"><title>Book Three</title></book>" +
        "</library>";

    private static (XmlDocument Doc, NodeId LibraryId, NodeId Book1, NodeId Book2, NodeId Book3) BuildDoc()
    {
        var (result, nsTable) = XmlDocument.ParseXml(LibraryXml);
        var doc = new XmlDocument(
            PlatformDocumentId.New(),
            source: null,
            "library.xml",
            result,
            nsTable);
        var libraryId = result.Document.DocumentElement!.Value;
        var library = (XdmElement)doc.Resolve(libraryId)!;
        return (doc, libraryId, library.Children[0], library.Children[1], library.Children[2]);
    }

    [Fact]
    public void SameParentReorderMovesSourceForward()
    {
        // Move book1 to index 2 (between book2 and book3) → after splice with adjustment,
        // expected order: book2, book1, book3.
        var (doc, libId, book1, book2, book3) = BuildDoc();

        var cmd = new MoveSubtreeCommand(doc, book1, libId, insertIndex: 2);
        doc.Undo.Execute(cmd);

        var lib = (XdmElement)doc.Resolve(libId)!;
        Assert.Equal(new[] { book2, book1, book3 }, lib.Children.ToArray());
    }

    [Fact]
    public void SameParentReorderMovesSourceBackward()
    {
        // Move book3 to index 0 → expected order: book3, book1, book2.
        var (doc, libId, book1, book2, book3) = BuildDoc();

        var cmd = new MoveSubtreeCommand(doc, book3, libId, insertIndex: 0);
        doc.Undo.Execute(cmd);

        var lib = (XdmElement)doc.Resolve(libId)!;
        Assert.Equal(new[] { book3, book1, book2 }, lib.Children.ToArray());
    }

    [Fact]
    public void MoveToDifferentParentReParentsAndUpdatesParentPointer()
    {
        // Move book1 to become the last child of book3 (re-parent within same doc).
        var (doc, libId, book1, _, book3) = BuildDoc();

        var cmd = new MoveSubtreeCommand(doc, book1, book3, insertIndex: int.MaxValue);
        doc.Undo.Execute(cmd);

        var lib = (XdmElement)doc.Resolve(libId)!;
        Assert.DoesNotContain(book1, lib.Children);

        var newBook3 = (XdmElement)doc.Resolve(book3)!;
        Assert.Contains(book1, newBook3.Children);
        Assert.Equal(book1, newBook3.Children[^1]);

        var movedBook1 = (XdmElement)doc.Resolve(book1)!;
        Assert.Equal(book3, movedBook1.Parent);
    }

    [Fact]
    public void MovePreservesNodeId()
    {
        var (doc, libId, book1, _, book3) = BuildDoc();
        var originalIdValue = book1.Value;

        doc.Undo.Execute(new MoveSubtreeCommand(doc, book1, book3, insertIndex: 0));

        var newBook3 = (XdmElement)doc.Resolve(book3)!;
        var movedId = newBook3.Children[0];
        Assert.Equal(originalIdValue, movedId.Value);
    }

    [Fact]
    public void CycleDropOntoSelfThrows()
    {
        var (doc, libId, book1, _, _) = BuildDoc();

        var cmd = new MoveSubtreeCommand(doc, book1, book1, insertIndex: 0);
        Assert.Throws<InvalidOperationException>(() => doc.Undo.Execute(cmd));
    }

    [Fact]
    public void CycleDropOntoDescendantThrows()
    {
        // book1 contains <title>; attempt to drop book1 into its own <title> child.
        var (doc, _, book1, _, _) = BuildDoc();
        var book1El = (XdmElement)doc.Resolve(book1)!;
        var titleId = book1El.Children[0];

        var cmd = new MoveSubtreeCommand(doc, book1, titleId, insertIndex: 0);
        Assert.Throws<InvalidOperationException>(() => doc.Undo.Execute(cmd));
    }

    [Fact]
    public void RevertRestoresOriginalOrder()
    {
        var (doc, libId, book1, book2, book3) = BuildDoc();
        var original = ((XdmElement)doc.Resolve(libId)!).Children.ToArray();

        doc.Undo.Execute(new MoveSubtreeCommand(doc, book1, libId, insertIndex: 2));
        doc.Undo.Undo();

        var lib = (XdmElement)doc.Resolve(libId)!;
        Assert.Equal(original, lib.Children.ToArray());
    }

    [Fact]
    public void RevertAcrossParentsRestoresBoth()
    {
        var (doc, libId, book1, _, book3) = BuildDoc();
        var origLib = ((XdmElement)doc.Resolve(libId)!).Children.ToArray();
        var origBook3 = ((XdmElement)doc.Resolve(book3)!).Children.ToArray();
        var origBook1Parent = ((XdmElement)doc.Resolve(book1)!).Parent;

        doc.Undo.Execute(new MoveSubtreeCommand(doc, book1, book3, insertIndex: 0));
        doc.Undo.Undo();

        var lib = (XdmElement)doc.Resolve(libId)!;
        var book3After = (XdmElement)doc.Resolve(book3)!;
        var book1After = (XdmElement)doc.Resolve(book1)!;

        Assert.Equal(origLib, lib.Children.ToArray());
        Assert.Equal(origBook3, book3After.Children.ToArray());
        Assert.Equal(origBook1Parent, book1After.Parent);
    }

    [Fact]
    public void RedoReappliesMove()
    {
        var (doc, libId, book1, book2, book3) = BuildDoc();

        doc.Undo.Execute(new MoveSubtreeCommand(doc, book1, libId, insertIndex: 2));
        doc.Undo.Undo();
        doc.Undo.Redo();

        var lib = (XdmElement)doc.Resolve(libId)!;
        Assert.Equal(new[] { book2, book1, book3 }, lib.Children.ToArray());
    }

    [Fact]
    public void TryMergeWithReturnsFalse()
    {
        var (doc, libId, book1, _, _) = BuildDoc();
        var cmd1 = new MoveSubtreeCommand(doc, book1, libId, insertIndex: 1);
        var cmd2 = new MoveSubtreeCommand(doc, book1, libId, insertIndex: 2);

        Assert.False(cmd2.TryMergeWith(cmd1, out _));
    }
}
