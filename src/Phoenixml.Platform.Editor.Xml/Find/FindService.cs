using System;
using System.Collections.Generic;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace Phoenixml.Platform.Editor.Xml.Find;

/// <summary>
/// Walks an <see cref="XmlDocument"/> and returns the <see cref="NodeId"/>s of all nodes
/// whose display-relevant text contains a given query string.
/// </summary>
/// <remarks>
/// Search scope (v0.8): element local names, attribute local names, attribute values, and
/// text-node values. Comments and processing instructions are deferred to Plan 17.
/// Results are returned in document order: attributes before children, children in source
/// order.
/// </remarks>
public sealed class FindService
{
    /// <summary>
    /// Searches <paramref name="doc"/> for nodes whose text contains <paramref name="query"/>.
    /// </summary>
    /// <param name="doc">The XML document to search. Must not be <see langword="null"/>.</param>
    /// <param name="query">The substring to search for. Returns an empty list when null or empty.</param>
    /// <param name="matchCase">
    /// <see langword="true"/> for an exact-case (<see cref="StringComparison.Ordinal"/>) search;
    /// <see langword="false"/> (the default) for a case-insensitive (<see cref="StringComparison.OrdinalIgnoreCase"/>) search.
    /// </param>
    /// <returns>
    /// An <see cref="IReadOnlyList{T}"/> of matching <see cref="NodeId"/>s in document order.
    /// Never <see langword="null"/>; empty when <paramref name="query"/> is null/empty or there are no matches.
    /// </returns>
    public static IReadOnlyList<NodeId> Find(XmlDocument doc, string? query, bool matchCase)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (string.IsNullOrEmpty(query)) return Array.Empty<NodeId>();

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var matches = new List<NodeId>();

        Walk(doc, doc.Root.Id, query, comparison, matches);

        return matches;
    }

    private static void Walk(
        XmlDocument doc,
        NodeId id,
        string query,
        StringComparison cmp,
        List<NodeId> matches)
    {
        var node = doc.Resolve(id);
        if (node is null) return;

        if (NodeMatches(node, query, cmp))
            matches.Add(id);

        if (node is XdmElement el)
        {
            // Attributes before children — matches provider document order.
            foreach (var attrId in el.Attributes)
                Walk(doc, attrId, query, cmp, matches);

            foreach (var childId in el.Children)
                Walk(doc, childId, query, cmp, matches);
        }
        else if (node is XdmDocument d)
        {
            foreach (var childId in d.Children)
                Walk(doc, childId, query, cmp, matches);
        }
    }

    private static bool NodeMatches(XdmNode node, string query, StringComparison cmp) =>
        node switch
        {
            XdmElement el =>
                el.LocalName.Contains(query, cmp),
            XdmAttribute attr =>
                attr.LocalName.Contains(query, cmp)
                || (attr.Value?.Contains(query, cmp) ?? false),
            XdmText t =>
                t.Value.Contains(query, cmp),
            _ => false  // XdmComment / XdmProcessingInstruction deferred to Plan 17
        };
}
