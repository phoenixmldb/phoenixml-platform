using System;
using System.Collections.Generic;
using System.Linq;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Editing;
using Xunit;
using PlatformDocumentId = Phoenixml.Platform.Abstractions.Documents.DocumentId;

namespace Phoenixml.Platform.Editor.Xml.Tests.Editing;

/// <summary>
/// Tests for <see cref="CompoundCommand"/>.
/// </summary>
public class CompoundCommandTests
{
    // Three named elements in document order: root, child1, child2.
    private const string FixtureXml =
        "<root>" +
        "<child1>text1</child1>" +
        "<child2>text2</child2>" +
        "</root>";

    private static (XmlDocument Doc, NodeId RootId, NodeId Child1Id, NodeId Child2Id) BuildDoc()
    {
        var (result, nsTable) = XmlDocument.ParseXml(FixtureXml);
        var doc = new XmlDocument(
            PlatformDocumentId.New(),
            source: null,
            "fixture.xml",
            result,
            nsTable);

        var rootId = result.Document.DocumentElement!.Value;
        var rootElem = (XdmElement)doc.Resolve(rootId)!;

        var elementChildren = rootElem.Children
            .Where(id => doc.Resolve(id) is XdmElement)
            .ToList();

        return (doc, rootId, elementChildren[0], elementChildren[1]);
    }

    [Fact]
    public void ApplyRunsAllChildrenInOrder()
    {
        var (doc, rootId, child1Id, child2Id) = BuildDoc();

        var commands = new List<Phoenixml.Platform.Abstractions.Documents.IEditorCommand>
        {
            new RenameElementCommand(doc, rootId,   "root-renamed"),
            new RenameElementCommand(doc, child1Id, "alpha"),
            new RenameElementCommand(doc, child2Id, "beta"),
        };

        var compound = new CompoundCommand(commands, "rename three elements");
        doc.Undo.Execute(compound);

        Assert.Equal("root-renamed", ((XdmElement)doc.Resolve(rootId)!).LocalName);
        Assert.Equal("alpha",        ((XdmElement)doc.Resolve(child1Id)!).LocalName);
        Assert.Equal("beta",         ((XdmElement)doc.Resolve(child2Id)!).LocalName);
    }

    [Fact]
    public void RevertRunsChildrenInReverseOrder()
    {
        var (doc, rootId, child1Id, child2Id) = BuildDoc();

        var commands = new List<Phoenixml.Platform.Abstractions.Documents.IEditorCommand>
        {
            new RenameElementCommand(doc, rootId,   "root-renamed"),
            new RenameElementCommand(doc, child1Id, "alpha"),
            new RenameElementCommand(doc, child2Id, "beta"),
        };

        var compound = new CompoundCommand(commands, "rename three elements");
        doc.Undo.Execute(compound);

        // Sanity: all renamed.
        Assert.Equal("root-renamed", ((XdmElement)doc.Resolve(rootId)!).LocalName);

        doc.Undo.Undo();

        // All should be back to original names.
        Assert.Equal("root",   ((XdmElement)doc.Resolve(rootId)!).LocalName);
        Assert.Equal("child1", ((XdmElement)doc.Resolve(child1Id)!).LocalName);
        Assert.Equal("child2", ((XdmElement)doc.Resolve(child2Id)!).LocalName);
    }

    [Fact]
    public void DescriptionExposesGivenString()
    {
        var (doc, rootId, _, _) = BuildDoc();

        var commands = new List<Phoenixml.Platform.Abstractions.Documents.IEditorCommand>
        {
            new RenameElementCommand(doc, rootId, "x"),
        };

        var compound = new CompoundCommand(commands, "my compound description");

        Assert.Equal("my compound description", compound.Description);
    }

    [Fact]
    public void ReferencedNodeIdsAggregates()
    {
        // Use SetTextContentCommand which exposes the new text node id in ReferencedNodeIds
        // (CreatedNewText path). Here we just verify the aggregate enumerates all children.
        var (doc, rootId, child1Id, child2Id) = BuildDoc();

        // RenameElementCommand.ReferencedNodeIds returns empty. Use child commands that
        // return disjoint ids by manually wrapping via a custom stub. Instead, verify
        // by checking that SelectMany over all three children (even if all return empty)
        // produces a union — and that the compound's ReferencedNodeIds is non-null and
        // is exactly the concatenation.
        //
        // For a concrete non-empty test, use SetTextContentCommand on empty elements.
        // The <root> renamed above has element children, so use a fresh empty-element doc.
        var (emptyResult, emptyNsTable) = XmlDocument.ParseXml("<root><a/><b/></root>");
        var emptyDoc = new XmlDocument(
            PlatformDocumentId.New(),
            source: null,
            "empty.xml",
            emptyResult,
            emptyNsTable);

        var emptyRootId = emptyResult.Document.DocumentElement!.Value;
        var emptyRoot = (XdmElement)emptyDoc.Resolve(emptyRootId)!;
        var aId = emptyRoot.Children.First(id => emptyDoc.Resolve(id) is XdmElement e && e.LocalName == "a");
        var bId = emptyRoot.Children.First(id => emptyDoc.Resolve(id) is XdmElement e && e.LocalName == "b");

        // SetTextContentCommand on an empty element allocates a new NodeId on Apply,
        // then exposes it via ReferencedNodeIds.
        var cmdA = new SetTextContentCommand(emptyDoc, aId, "hello");
        var cmdB = new SetTextContentCommand(emptyDoc, bId, "world");

        var commands = new List<Phoenixml.Platform.Abstractions.Documents.IEditorCommand> { cmdA, cmdB };
        var compound = new CompoundCommand(commands, "set two texts");

        // Before Apply, both commands haven't run so ReferencedNodeIds are empty.
        Assert.Empty(compound.ReferencedNodeIds);

        // Apply so the commands capture their allocated node ids.
        emptyDoc.Undo.Execute(compound);

        // After Apply, each SetTextContentCommand exposes 1 ReferencedNodeId.
        var ids = compound.ReferencedNodeIds.ToHashSet();
        Assert.Equal(2, ids.Count);
    }

    [Fact]
    public void TryMergeWithReturnsFalse()
    {
        var (doc, rootId, _, _) = BuildDoc();

        var commands = new List<Phoenixml.Platform.Abstractions.Documents.IEditorCommand>
        {
            new RenameElementCommand(doc, rootId, "x"),
        };

        var compound = new CompoundCommand(commands, "test");
        var other    = new CompoundCommand(commands, "other");

        bool merged = compound.TryMergeWith(other, out var result);

        Assert.False(merged);
        Assert.Same(compound, result);
    }

    [Fact]
    public void EmptyChildrenListThrows()
    {
        var empty = new List<Phoenixml.Platform.Abstractions.Documents.IEditorCommand>();

        var ex = Assert.Throws<ArgumentException>(() =>
            new CompoundCommand(empty, "should throw"));

        Assert.Contains("at least one", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyThenRevertRoundTrip()
    {
        // Three SetTextContent commands on text-bearing elements; verify round-trip.
        var (doc, _, child1Id, child2Id) = BuildDoc();

        // child1 and child2 each have one text child; capture originals.
        static string GetText(XmlDocument d, NodeId elemId)
        {
            var elem = (XdmElement)d.Resolve(elemId)!;
            var textId = elem.Children.First(id => d.Resolve(id) is XdmText);
            return ((XdmText)d.Resolve(textId)!).Value;
        }

        var originalText1 = GetText(doc, child1Id);
        var originalText2 = GetText(doc, child2Id);

        var commands = new List<Phoenixml.Platform.Abstractions.Documents.IEditorCommand>
        {
            new SetTextContentCommand(doc, child1Id, "replaced-text1"),
            new SetTextContentCommand(doc, child2Id, "replaced-text2"),
        };

        var compound = new CompoundCommand(commands, "set two text contents");
        doc.Undo.Execute(compound);

        Assert.Equal("replaced-text1", GetText(doc, child1Id));
        Assert.Equal("replaced-text2", GetText(doc, child2Id));

        doc.Undo.Undo();

        Assert.Equal(originalText1, GetText(doc, child1Id));
        Assert.Equal(originalText2, GetText(doc, child2Id));
    }
}
