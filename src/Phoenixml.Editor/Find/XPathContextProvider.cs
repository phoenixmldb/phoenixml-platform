using System;
using System.Collections.Generic;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml;

namespace Phoenixml.Editor.Find;

/// <summary>Kind of token the caret is currently positioned to complete.</summary>
public enum XPathContextKind
{
    /// <summary>No completion applies.</summary>
    None,
    /// <summary>Standalone word — complete against XPath function names.</summary>
    FunctionName,
    /// <summary>Word immediately following <c>/</c> — complete against element names.</summary>
    ElementName,
    /// <summary>Word immediately following <c>@</c> — complete against attribute names.</summary>
    AttributeName,
}

/// <summary>Detected context at the caret: kind + the prefix typed so far.</summary>
public sealed record XPathContext(XPathContextKind Kind, string Prefix);

/// <summary>
/// Caret-aware token-classifier for the XPath query bar. Decides whether the prefix at
/// the caret should be completed against function names, element names, or attribute names.
/// Also exposes harvest helpers for collecting element / attribute local names from a doc.
/// </summary>
public static class XPathContextProvider
{
    /// <summary>Classifies the token at <paramref name="caret"/> in <paramref name="text"/>.</summary>
    public static XPathContext DetectContext(string? text, int caret)
    {
        if (string.IsNullOrEmpty(text) || caret < 0 || caret > text.Length)
            return new XPathContext(XPathContextKind.None, string.Empty);

        int start = caret;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        var prefix = text.Substring(start, caret - start);

        if (start == 0)
            return new XPathContext(
                prefix.Length > 0 ? XPathContextKind.FunctionName : XPathContextKind.None,
                prefix);

        var before = text[start - 1];
        if (before == '@')
            return new XPathContext(XPathContextKind.AttributeName, prefix);
        if (before == '/')
            return new XPathContext(XPathContextKind.ElementName, prefix);

        return new XPathContext(XPathContextKind.FunctionName, prefix);
    }

    /// <summary>Collects distinct element local names from <paramref name="doc"/>.</summary>
    public static IReadOnlyList<string> CollectElementNames(XmlDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var set = new HashSet<string>(StringComparer.Ordinal);
        Walk(doc, doc.Root.Id, set, attributes: false);
        var list = new List<string>(set);
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    /// <summary>Collects distinct attribute local names from <paramref name="doc"/>.</summary>
    public static IReadOnlyList<string> CollectAttributeNames(XmlDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var set = new HashSet<string>(StringComparer.Ordinal);
        Walk(doc, doc.Root.Id, set, attributes: true);
        var list = new List<string>(set);
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static void Walk(XmlDocument doc, PhoenixmlDb.Core.NodeId id, HashSet<string> set, bool attributes)
    {
        var node = doc.Resolve(id);
        if (node is null) return;
        if (node is XdmElement el)
        {
            if (!attributes) set.Add(el.LocalName);
            if (attributes)
            {
                foreach (var attrId in el.Attributes)
                {
                    if (doc.Resolve(attrId) is XdmAttribute attr)
                        set.Add(attr.LocalName);
                }
            }
            foreach (var childId in el.Children)
                Walk(doc, childId, set, attributes);
        }
        else if (node is XdmDocument d)
        {
            foreach (var childId in d.Children)
                Walk(doc, childId, set, attributes);
        }
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ':';
}
