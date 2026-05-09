using System.Linq;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml;
using Xunit;
using PlatformDocumentId = Phoenixml.Platform.Abstractions.Documents.DocumentId;

namespace Phoenixml.Platform.Editor.Xml.Tests;

/// <summary>
/// Tests for <see cref="XmlDocument.AllocateNodeId"/> and <see cref="XmlDocument.AddNode"/>.
/// </summary>
public class XmlDocumentAllocatorTests
{
    private const string SimpleXml = "<root><child>text</child></root>";

    private static XmlDocument BuildDoc()
    {
        var (result, nsTable) = XmlDocument.ParseXml(SimpleXml);
        return new XmlDocument(
            PlatformDocumentId.New(),
            source: null,
            "test.xml",
            result,
            nsTable);
    }

    [Fact]
    public void AllocateNodeIdReturnsMonotonicIds()
    {
        var doc = BuildDoc();

        var id1 = doc.AllocateNodeId();
        var id2 = doc.AllocateNodeId();
        var id3 = doc.AllocateNodeId();

        Assert.True(id1.Value < id2.Value, "Second id must exceed first.");
        Assert.True(id2.Value < id3.Value, "Third id must exceed second.");
    }

    [Fact]
    public void AllocateNodeIdFirstAllocationExceedsMaxParsedId()
    {
        var (result, nsTable) = XmlDocument.ParseXml(SimpleXml);
        var doc = new XmlDocument(
            PlatformDocumentId.New(),
            source: null,
            "test.xml",
            result,
            nsTable);

        // Determine the maximum NodeId that the parser assigned.
        ulong maxParsed = result.Nodes.Max(n => n.Id.Value);

        var allocated = doc.AllocateNodeId();

        Assert.True(allocated.Value > maxParsed,
            $"Allocated id {allocated.Value} must exceed max parsed id {maxParsed}.");
    }

    [Fact]
    public void AddNodeAddsToById()
    {
        var doc = BuildDoc();

        var newId = doc.AllocateNodeId();
        var textNode = new XdmText
        {
            Id = newId,
            Document = doc.Root.Document,   // XdmNode.Document is the DocumentId
            Parent = default,
            Value = "hello",
        };

        doc.AddNode(textNode);

        var resolved = doc.Resolve(newId);
        Assert.NotNull(resolved);
        var resolvedText = Assert.IsType<XdmText>(resolved);
        Assert.Equal("hello", resolvedText.Value);
    }

    [Fact]
    public void AddNodeDuplicateIdThrows()
    {
        var doc = BuildDoc();

        // Resolve a node that already exists, then try to add it again.
        var existingId = doc.Root.DocumentElement!.Value;
        var existingNode = doc.Resolve(existingId)!;

        var ex = Assert.Throws<InvalidOperationException>(() => doc.AddNode(existingNode));
        Assert.Contains("already present", ex.Message, StringComparison.Ordinal);
    }
}
