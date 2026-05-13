using System.Collections.Generic;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml.Diff;
using Phoenixml.Platform.Editor.Xml.Navigation;
using Xunit;

namespace Phoenixml.Platform.Editor.Xml.Tests.Diff;

public class DiffIndexBuilderTests
{
    private static XmlDocument Parse(string xml)
    {
        var (result, nsTable) = XmlDocument.ParseXml(xml);
        return new XmlDocument(
            Phoenixml.Platform.Abstractions.Documents.DocumentId.New(),
            source: null, displayName: "doc.xml", result, nsTable);
    }

    [Fact]
    public void IdenticalDocsProduceEmptyIndex()
    {
        var a = Parse("<r><x/></r>");
        var b = Parse("<r><x/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        var leftIdx = DiffIndexBuilder.Build(a, diff, side: DiffSide.Left);
        Assert.Equal(0, leftIdx.Count);
    }

    [Fact]
    public void RemovedElementShowsLeftSideOnly()
    {
        var a = Parse("<r><x/><y/></r>");
        var b = Parse("<r><x/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        var leftIdx = DiffIndexBuilder.Build(a, diff, side: DiffSide.Left);
        var rightIdx = DiffIndexBuilder.Build(b, diff, side: DiffSide.Right);

        var yId = ResolveByXPath(a, "/r/y");
        Assert.Equal(DiffKind.Removed, leftIdx.GetKind(yId));
        Assert.Equal(1, leftIdx.Count);
        Assert.Equal(0, rightIdx.Count);
    }

    [Fact]
    public void AddedElementShowsRightSideOnly()
    {
        var a = Parse("<r><x/></r>");
        var b = Parse("<r><x/><z/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        var leftIdx = DiffIndexBuilder.Build(a, diff, side: DiffSide.Left);
        var rightIdx = DiffIndexBuilder.Build(b, diff, side: DiffSide.Right);

        var zId = ResolveByXPath(b, "/r/z");
        Assert.Equal(DiffKind.Added, rightIdx.GetKind(zId));
        Assert.Equal(0, leftIdx.Count);
        Assert.Equal(1, rightIdx.Count);
    }

    [Fact]
    public void ChangedAttributeShowsBothSides()
    {
        var a = Parse("<r><x a=\"1\"/></r>");
        var b = Parse("<r><x a=\"2\"/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        var leftIdx = DiffIndexBuilder.Build(a, diff, side: DiffSide.Left);
        var rightIdx = DiffIndexBuilder.Build(b, diff, side: DiffSide.Right);

        var aLeft = ResolveByXPath(a, "/r/x/@a");
        var aRight = ResolveByXPath(b, "/r/x/@a");
        Assert.Equal(DiffKind.Changed, leftIdx.GetKind(aLeft));
        Assert.Equal(DiffKind.Changed, rightIdx.GetKind(aRight));
    }

    private static NodeId ResolveByXPath(XmlDocument doc, string xpath)
    {
        foreach (var node in EnumerateAll(doc))
        {
            if (XPathBuilder.Build(doc, node.Id) == xpath)
                return node.Id;
        }
        throw new Xunit.Sdk.XunitException($"No node at {xpath}");
    }

    private static IEnumerable<XdmNode> EnumerateAll(XmlDocument doc)
    {
        var stack = new Stack<NodeId>();
        stack.Push(doc.Root.Id);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            var node = doc.Resolve(id);
            if (node is null) continue;
            yield return node;
            if (node is XdmElement el)
            {
                foreach (var a in el.Attributes) stack.Push(a);
                foreach (var c in el.Children) stack.Push(c);
            }
            else if (node is XdmDocument d)
            {
                foreach (var c in d.Children) stack.Push(c);
            }
        }
    }
}
