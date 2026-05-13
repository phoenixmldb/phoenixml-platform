using System.Linq;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml.Diff;
using Xunit;

namespace Phoenixml.Platform.Editor.Xml.Tests.Diff;

public class XmlDocumentDiffTests
{
    private static XmlDocument Parse(string xml)
    {
        var (result, nsTable) = XmlDocument.ParseXml(xml);
        return new XmlDocument(
            Phoenixml.Platform.Abstractions.Documents.DocumentId.New(),
            source: null,
            displayName: "doc.xml",
            result, nsTable);
    }

    [Fact]
    public void IdenticalDocsHaveNoDifferences()
    {
        var a = Parse("<r><x/></r>");
        var b = Parse("<r><x/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        Assert.Empty(diff.Changes);
    }

    [Fact]
    public void AddedElementShowsAsAdded()
    {
        var a = Parse("<r><x/></r>");
        var b = Parse("<r><x/><y/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        var change = Assert.Single(diff.Changes);
        Assert.Equal(DiffKind.Added, change.Kind);
        Assert.Equal("/r/y", change.XPath);
    }

    [Fact]
    public void RemovedElementShowsAsRemoved()
    {
        var a = Parse("<r><x/><y/></r>");
        var b = Parse("<r><x/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        var change = Assert.Single(diff.Changes);
        Assert.Equal(DiffKind.Removed, change.Kind);
        Assert.Equal("/r/y", change.XPath);
    }

    [Fact]
    public void ChangedAttributeShowsAsChanged()
    {
        var a = Parse("<r><x a=\"1\"/></r>");
        var b = Parse("<r><x a=\"2\"/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        var change = Assert.Single(diff.Changes);
        Assert.Equal(DiffKind.Changed, change.Kind);
        Assert.Equal("/r/x/@a", change.XPath);
        Assert.Equal("1", change.LeftValue);
        Assert.Equal("2", change.RightValue);
    }

    [Fact]
    public void ChangedTextShowsAsChanged()
    {
        var a = Parse("<r><x>old</x></r>");
        var b = Parse("<r><x>new</x></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        var change = Assert.Single(diff.Changes);
        Assert.Equal(DiffKind.Changed, change.Kind);
        Assert.Equal("/r/x", change.XPath);
        Assert.Equal("old", change.LeftValue);
        Assert.Equal("new", change.RightValue);
    }

    [Fact]
    public void DiffSurfacesBothAddedAndRemovedAtSameParent()
    {
        var a = Parse("<r><x/><y/></r>");
        var b = Parse("<r><x/><z/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        Assert.Equal(2, diff.Changes.Count);
        Assert.Contains(diff.Changes, c => c.Kind == DiffKind.Removed && c.XPath == "/r/y");
        Assert.Contains(diff.Changes, c => c.Kind == DiffKind.Added && c.XPath == "/r/z");
    }
}
