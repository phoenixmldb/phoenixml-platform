using System.Collections.Generic;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace Phoenixml.Platform.Editor.Xml.Navigation;

/// <summary>
/// Builds a canonical XPath string from an <see cref="XmlDocument"/> + <see cref="NodeId"/>.
/// </summary>
/// <remarks>
/// <para>
/// The returned XPath walks up via <see cref="XdmNode.Parent"/> pointers, prepending each
/// ancestor as a step. Positional predicates (e.g. <c>book[2]</c>) are emitted only when
/// the element has at least one same-name sibling — single occurrences render without the
/// <c>[1]</c> for readability.
/// </para>
/// <para>
/// Step kinds:
/// <list type="bullet">
///   <item><description>Element → <c>localName</c> or <c>localName[N]</c>.</description></item>
///   <item><description>Attribute → <c>@localName</c>.</description></item>
///   <item><description>Text → <c>text()</c>.</description></item>
///   <item><description>Comment → <c>comment()</c>.</description></item>
///   <item><description>Processing instruction → <c>processing-instruction('target')</c>.</description></item>
/// </list>
/// </para>
/// <para>
/// Read-only: never mutates the document.
/// </para>
/// </remarks>
public static class XPathBuilder
{
    /// <summary>
    /// Builds the canonical XPath for <paramref name="targetId"/> within <paramref name="doc"/>.
    /// </summary>
    /// <param name="doc">The XML document.</param>
    /// <param name="targetId">The node to describe.</param>
    /// <returns>
    /// The canonical XPath string (e.g. <c>/library/book[2]/title</c>), or an empty string
    /// when <paramref name="targetId"/> cannot be resolved.
    /// </returns>
    public static string Build(XmlDocument doc, NodeId targetId)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var target = doc.Resolve(targetId);
        if (target is null) return string.Empty;
        if (target is XdmDocument) return "/";

        var segments = new List<string>();
        XdmNode? current = target;
        while (current is not null && current is not XdmDocument)
        {
            segments.Insert(0, BuildSegment(doc, current));
            current = current.Parent is NodeId p ? doc.Resolve(p) : null;
        }

        return "/" + string.Join("/", segments);
    }

    private static string BuildSegment(XmlDocument doc, XdmNode node)
    {
        switch (node)
        {
            case XdmAttribute a:
                return "@" + a.LocalName;

            case XdmElement el:
                {
                    var name = el.LocalName;
                    if (el.Parent is NodeId pid && doc.Resolve(pid) is XdmElement parent)
                    {
                        int sameNameCount = 0;
                        int indexAmongSameName = 0;
                        foreach (var sib in parent.Children)
                        {
                            if (doc.Resolve(sib) is XdmElement sibEl && sibEl.LocalName == name)
                            {
                                sameNameCount++;
                                if (sibEl.Id == el.Id) indexAmongSameName = sameNameCount;
                            }
                        }
                        if (sameNameCount > 1) return $"{name}[{indexAmongSameName}]";
                    }
                    return name;
                }

            case XdmText _:
                return "text()";

            case XdmComment _:
                return "comment()";

            case XdmProcessingInstruction pi:
                return $"processing-instruction('{pi.Target}')";

            default:
                return node.GetType().Name;
        }
    }
}
