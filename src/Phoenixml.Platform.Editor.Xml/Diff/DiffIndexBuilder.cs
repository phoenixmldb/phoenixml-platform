using System;
using System.Collections.Generic;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml.Navigation;

namespace Phoenixml.Platform.Editor.Xml.Diff;

/// <summary>
/// Resolves <see cref="DiffChange"/>s back to <see cref="NodeId"/>s in a specific document
/// so the dual-pane compare view can tint rows by diff kind.
/// </summary>
/// <remarks>
/// The diff is keyed by canonical XPath. This builder walks the document, computes each
/// node's XPath, and stamps the matching diff kind into a <see cref="DiffIndex"/>.
/// Per side: left-side nodes are stamped for Removed and Changed; right-side nodes are
/// stamped for Added and Changed. (A change shows on both sides.)
/// </remarks>
public static class DiffIndexBuilder
{
    /// <summary>Builds the index for one side of a comparison.</summary>
    public static DiffIndex Build(XmlDocument doc, XmlDiffResult diff, DiffSide side)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(diff);

        var wanted = new Dictionary<string, DiffKind>(StringComparer.Ordinal);
        foreach (var change in diff.Changes)
        {
            var include = (side, change.Kind) switch
            {
                (DiffSide.Left, DiffKind.Removed) => true,
                (DiffSide.Left, DiffKind.Changed) => true,
                (DiffSide.Right, DiffKind.Added) => true,
                (DiffSide.Right, DiffKind.Changed) => true,
                _ => false,
            };
            if (include) wanted[change.XPath] = change.Kind;
        }

        var entries = new Dictionary<NodeId, DiffKind>();
        if (wanted.Count > 0)
            Walk(doc, doc.Root.Id, wanted, entries);

        var idx = new DiffIndex();
        idx.Reset(entries);
        return idx;
    }

    private static void Walk(
        XmlDocument doc,
        NodeId id,
        IReadOnlyDictionary<string, DiffKind> wanted,
        Dictionary<NodeId, DiffKind> entries)
    {
        var node = doc.Resolve(id);
        if (node is null) return;

        if (node is XdmElement el)
        {
            var xpath = XPathBuilder.Build(doc, id);
            if (wanted.TryGetValue(xpath, out var kind))
                entries[id] = kind;

            foreach (var attrId in el.Attributes)
            {
                if (doc.Resolve(attrId) is XdmAttribute)
                {
                    var attrXpath = XPathBuilder.Build(doc, attrId);
                    if (wanted.TryGetValue(attrXpath, out var attrKind))
                        entries[attrId] = attrKind;
                }
            }

            foreach (var childId in el.Children)
                Walk(doc, childId, wanted, entries);
        }
        else if (node is XdmDocument d)
        {
            foreach (var childId in d.Children)
                Walk(doc, childId, wanted, entries);
        }
    }
}
