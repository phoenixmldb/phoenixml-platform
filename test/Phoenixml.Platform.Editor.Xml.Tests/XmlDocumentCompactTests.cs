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

namespace Phoenixml.Platform.Editor.Xml.Tests;

/// <summary>
/// Tests for <see cref="XmlDocument.Compact"/>.
/// </summary>
public class XmlDocumentCompactTests
{
    private const string CatalogXml =
        "<catalog>" +
        "<product id=\"p1\"><name>Widget</name></product>" +
        "<product id=\"p2\"><name>Gadget</name></product>" +
        "</catalog>";

    private static XmlDocument BuildDoc()
    {
        var (result, nsTable) = XmlDocument.ParseXml(CatalogXml);
        return new XmlDocument(
            PlatformDocumentId.New(),
            source: null,
            "catalog.xml",
            result,
            nsTable);
    }

    [Fact]
    public void CompactPreservesAllNodesReachableFromRoot()
    {
        var doc = BuildDoc();
        var countBefore = doc.Root.Children.Count; // sanity: document has children

        // Record the number of nodes registered during parse.
        var idsBefore = GetByIdCount(doc);

        doc.Compact();

        // Nothing should be evicted — no orphans were introduced.
        Assert.Equal(idsBefore, GetByIdCount(doc));
        Assert.True(countBefore > 0);
    }

    [Fact]
    public void CompactPreservesDeletedSubtreeWhileDeleteCommandOnUndoStack()
    {
        var doc = BuildDoc();
        var rootId = doc.Root.DocumentElement!.Value;
        var rootElem = (XdmElement)doc.Resolve(rootId)!;

        // Delete the first product element.
        var productId = rootElem.Children.First(id => doc.Resolve(id) is XdmElement);
        var cmd = new DeleteNodeCommand(doc, productId);
        doc.Undo.Execute(cmd);

        doc.Compact();

        // The deleted product node must still be resolvable because the DeleteNodeCommand
        // is on the undo stack and its ReferencedNodeIds pins the subtree.
        Assert.NotNull(doc.Resolve(productId));
    }

    [Fact]
    public void CompactEvictsOrphansNotReferencedByAnyCommand()
    {
        var doc = BuildDoc();

        // Manually register a brand-new node that is never spliced into the tree
        // and is not referenced by any command — a pure orphan.
        var orphanId = doc.AllocateNodeId();
        var orphan = new XdmText
        {
            Id = orphanId,
            Document = doc.Root.Document,
            Value = "orphan text",
        };
        doc.AddNode(orphan);

        // Confirm the orphan is registered before compaction.
        Assert.NotNull(doc.Resolve(orphanId));

        doc.Compact();

        // After compaction the orphan must be gone: it is unreachable from root
        // and no command on undo/redo stacks references it.
        Assert.Null(doc.Resolve(orphanId));
    }

    [Fact]
    public void CompactRoundTripDoesNotChangeSerialization()
    {
        var doc = BuildDoc();
        var before = doc.SerializeToString();

        doc.Compact();

        var after = doc.SerializeToString();
        Assert.Equal(before, after, StringComparer.Ordinal);
    }

    [Fact]
    public async Task SaveTriggersCompactEvictingOrphans()
    {
        var doc = BuildDoc();

        // Register an orphan.
        var orphanId = doc.AllocateNodeId();
        var orphan = new XdmText
        {
            Id = orphanId,
            Document = doc.Root.Document,
            Value = "orphan text",
        };
        doc.AddNode(orphan);
        Assert.NotNull(doc.Resolve(orphanId));

        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "out.xml");
        try
        {
            await doc.SaveAsAsync(new Uri(path), TestContext.Current.CancellationToken).ConfigureAwait(true);

            // After save the orphan must have been swept.
            Assert.Null(doc.Resolve(orphanId));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns the number of entries currently in <c>_byId</c>. Uses the public
    /// <see cref="XmlDocument.Resolve"/> indirectly: we infer the count by the fact
    /// that Compact is idempotent and we can probe before/after. For direct count we
    /// use <see cref="XmlDocument.SerializeToString"/> as a proxy for reachability, but
    /// for counting we rely on a small helper approach: allocate a sentinel, check it is
    /// there, compact, check it is gone — which the <see cref="CompactEvictsOrphansNotReferencedByAnyCommand"/>
    /// test already covers. Here we need a raw count; expose it via reflection-free
    /// enumeration using AllocateNodeId's seeding behaviour, which reads max(_byId.Keys).
    /// Instead, just do it by counting probes: not efficient but accurate for test scale.
    ///
    /// Actually: the cleanest count is obtained by scanning ids 0..N. But we don't have
    /// the count exposed. Workaround: call AllocateNodeId to read the internal high-water
    /// mark, then we know the id space is [1 .. highWaterMark-1].  Count how many resolve.
    /// </summary>
    private static int GetByIdCount(XmlDocument doc)
    {
        // Allocate to seed the counter, then count how many ids below it resolve.
        var highWater = doc.AllocateNodeId();
        var count = 0;
        for (ulong v = 1; v < highWater.Value; v++)
        {
            if (doc.Resolve(new NodeId(v)) is not null) count++;
        }
        return count;
    }
}
