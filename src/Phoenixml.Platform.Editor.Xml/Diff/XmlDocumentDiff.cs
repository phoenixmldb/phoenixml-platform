using System;
using System.Collections.Generic;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml.Navigation;

namespace Phoenixml.Platform.Editor.Xml.Diff;

/// <summary>Kind of difference between two documents.</summary>
public enum DiffKind
{
    /// <summary>Node present only in the right (target) document.</summary>
    Added,
    /// <summary>Node present only in the left (source) document.</summary>
    Removed,
    /// <summary>Node value (attribute or shallow text) differs.</summary>
    Changed,
}

/// <summary>
/// A single structural difference between two documents, keyed by canonical XPath.
/// </summary>
public sealed record DiffChange(
    DiffKind Kind,
    string XPath,
    string? LeftValue,
    string? RightValue);

/// <summary>Result of a structural diff between two documents.</summary>
public sealed record XmlDiffResult(IReadOnlyList<DiffChange> Changes);

/// <summary>
/// MVP structural diff. Matches nodes by canonical XPath, reports additions, removals,
/// and attribute / shallow-text value changes. Element renames manifest as Removed+Added
/// at the same parent. Order-of-children diffs are not surfaced — only set membership.
/// </summary>
public static class XmlDocumentDiff
{
    /// <summary>Computes the diff. The result is independent of input documents.</summary>
    public static XmlDiffResult Compute(XmlDocument left, XmlDocument right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftMap = BuildMap(left);
        var rightMap = BuildMap(right);

        var changes = new List<DiffChange>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (xpath, leftVal) in leftMap)
        {
            seen.Add(xpath);
            if (rightMap.TryGetValue(xpath, out var rightVal))
            {
                if (!string.Equals(leftVal, rightVal, StringComparison.Ordinal))
                    changes.Add(new DiffChange(DiffKind.Changed, xpath, leftVal, rightVal));
            }
            else
            {
                changes.Add(new DiffChange(DiffKind.Removed, xpath, leftVal, null));
            }
        }

        foreach (var (xpath, rightVal) in rightMap)
        {
            if (seen.Contains(xpath)) continue;
            changes.Add(new DiffChange(DiffKind.Added, xpath, null, rightVal));
        }

        return new XmlDiffResult(changes);
    }

    private static Dictionary<string, string?> BuildMap(XmlDocument doc)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        Walk(doc, doc.Root.Id, map);
        return map;
    }

    private static void Walk(XmlDocument doc, NodeId id, Dictionary<string, string?> map)
    {
        var node = doc.Resolve(id);
        if (node is null) return;

        if (node is XdmElement el)
        {
            var xpath = XPathBuilder.Build(doc, id);
            // Shallow text only — element-as-container changes manifest via descendants.
            map[xpath] = ShallowText(doc, el);

            foreach (var attrId in el.Attributes)
            {
                if (doc.Resolve(attrId) is XdmAttribute attr)
                {
                    var attrPath = XPathBuilder.Build(doc, attrId);
                    map[attrPath] = attr.Value;
                }
            }

            foreach (var childId in el.Children)
                Walk(doc, childId, map);
        }
        else if (node is XdmDocument d)
        {
            foreach (var childId in d.Children)
                Walk(doc, childId, map);
        }
    }

    private static string ShallowText(XmlDocument doc, XdmElement el)
    {
        var sb = new StringBuilder();
        foreach (var childId in el.Children)
        {
            if (doc.Resolve(childId) is XdmText t)
                sb.Append(t.Value);
        }
        return sb.ToString();
    }
}
