using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;

namespace Phoenixml.Platform.Editor.Xml.Clipboard;

/// <summary>
/// Serializes an in-memory <see cref="XdmElement"/> subtree to a standalone XML string.
/// No XML declaration is emitted; the element and its descendants are written inline so
/// the result can be placed on the clipboard or embedded in any XML context.
/// </summary>
/// <remarks>
/// <para>
/// Namespace declarations that appear on <em>ancestor</em> elements (outside the subtree
/// being serialized) are synthesized on the serialized root so the output is self-contained,
/// parseable XML. All namespace bindings visible at the subtree root are included on the
/// output root element, even if they are not actually referenced inside the subtree
/// (bloated but always correct per Plan 18 v0.11 decision).
/// </para>
/// </remarks>
public static class SubtreeSerializer
{
    /// <summary>
    /// Serializes the element identified by <paramref name="rootId"/> and its entire
    /// descendant subtree to a standalone XML string with no <c>&lt;?xml?&gt;</c> declaration.
    /// </summary>
    /// <param name="doc">The document that contains the target element.</param>
    /// <param name="rootId">The <see cref="NodeId"/> of the element to serialize as the root.</param>
    /// <returns>
    /// An indented XML string containing the element and all its descendants,
    /// with no XML declaration.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="doc"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No node with <paramref name="rootId"/> exists in <paramref name="doc"/>.</exception>
    /// <exception cref="ArgumentException">The node identified by <paramref name="rootId"/> is not an <see cref="XdmElement"/>.</exception>
    public static string Serialize(XmlDocument doc, NodeId rootId)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var node = doc.Resolve(rootId)
            ?? throw new InvalidOperationException($"Node {rootId} not found in document.");

        if (node is not XdmElement element)
            throw new ArgumentException(
                $"Node {rootId} is not an element (kind: {node.NodeKind}).", nameof(rootId));

        // Collect namespace bindings declared on ancestor elements so we can synthesize
        // them on the output root. This ensures the serialized output is self-contained XML
        // even when the original subtree uses prefixes declared outside it.
        var ancestorDecls = CollectAncestorNamespaceDeclarations(doc, element);

        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true,
            Encoding = Encoding.UTF8,
            ConformanceLevel = ConformanceLevel.Fragment,
        };

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, settings))
        {
            WriteElement(writer, doc, element, ancestorDecls, isRoot: true);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Writes a single element node and all its descendants to <paramref name="writer"/>.
    /// On the root element, synthesized ancestor namespace declarations are injected first.
    /// </summary>
    private static void WriteElement(
        XmlWriter writer,
        XmlDocument doc,
        XdmElement element,
        Dictionary<string, NamespaceBinding>? ancestorDecls,
        bool isRoot)
    {
        var namespaceUri = doc.ResolveNamespace(element.Namespace) ?? string.Empty;
        var prefix = element.Prefix;

        if (prefix != null)
            writer.WriteStartElement(prefix, element.LocalName, namespaceUri);
        else if (!string.IsNullOrEmpty(namespaceUri))
            writer.WriteStartElement(element.LocalName, namespaceUri);
        else
            writer.WriteStartElement(element.LocalName);

        // On the root, synthesize ancestor namespace declarations first, then the
        // element's own declarations (own declarations override ancestors).
        if (isRoot && ancestorDecls != null && ancestorDecls.Count > 0)
        {
            // Collect prefixes already declared on this element's own NamespaceDeclarations.
            var alreadyDeclared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var nb in element.NamespaceDeclarations)
                alreadyDeclared.Add(nb.Prefix);

            foreach (var kvp in ancestorDecls)
            {
                if (alreadyDeclared.Contains(kvp.Key)) continue;
                var nsUri = doc.ResolveNamespace(kvp.Value.Namespace) ?? string.Empty;
                if (string.IsNullOrEmpty(kvp.Key))
                    writer.WriteAttributeString("xmlns", nsUri);
                else
                    writer.WriteAttributeString("xmlns", kvp.Key, null, nsUri);
            }
        }

        // Write namespace declarations declared on this element.
        foreach (var nsDecl in element.NamespaceDeclarations)
        {
            var nsUri = doc.ResolveNamespace(nsDecl.Namespace) ?? string.Empty;
            if (string.IsNullOrEmpty(nsDecl.Prefix))
                writer.WriteAttributeString("xmlns", nsUri);
            else
                writer.WriteAttributeString("xmlns", nsDecl.Prefix, null, nsUri);
        }

        // Write attributes.
        foreach (var attrId in element.Attributes)
        {
            if (doc.Resolve(attrId) is not XdmAttribute attr) continue;
            var attrNsUri = doc.ResolveNamespace(attr.Namespace) ?? string.Empty;

            if (attr.Prefix != null)
                writer.WriteAttributeString(attr.Prefix, attr.LocalName, attrNsUri, attr.Value);
            else if (!string.IsNullOrEmpty(attrNsUri))
                writer.WriteAttributeString(attr.LocalName, attrNsUri, attr.Value);
            else
                writer.WriteAttributeString(attr.LocalName, attr.Value);
        }

        // Write children.
        foreach (var childId in element.Children)
        {
            if (doc.Resolve(childId) is not XdmNode child) continue;
            WriteNode(writer, doc, child);
        }

        writer.WriteEndElement();
    }

    /// <summary>Dispatches to the appropriate write method based on node kind.</summary>
    private static void WriteNode(XmlWriter writer, XmlDocument doc, XdmNode node)
    {
        switch (node)
        {
            case XdmElement el:
                WriteElement(writer, doc, el, ancestorDecls: null, isRoot: false);
                break;

            case XdmText text:
                writer.WriteString(text.Value);
                break;

            case XdmComment comment:
                writer.WriteComment(comment.Value);
                break;

            case XdmProcessingInstruction pi:
                writer.WriteProcessingInstruction(pi.Target, pi.Value);
                break;
        }
    }

    /// <summary>
    /// Walks the ancestor chain of <paramref name="element"/> (not including the element
    /// itself) and returns all <see cref="NamespaceBinding"/> values declared on those
    /// ancestors, keyed by prefix. Closer ancestors override farther ones.
    /// </summary>
    private static Dictionary<string, NamespaceBinding> CollectAncestorNamespaceDeclarations(
        XmlDocument doc, XdmElement element)
    {
        var stack = new Stack<XdmElement>();
        var current = element.Parent;
        while (current.HasValue)
        {
            if (doc.Resolve(current.Value) is XdmElement ancestor)
            {
                stack.Push(ancestor);
                current = ancestor.Parent;
            }
            else
            {
                break;
            }
        }

        // Process outermost-first so inner ancestor declarations override outer ones.
        var result = new Dictionary<string, NamespaceBinding>(StringComparer.Ordinal);
        while (stack.Count > 0)
        {
            var ancestor = stack.Pop();
            foreach (var binding in ancestor.NamespaceDeclarations)
                result[binding.Prefix] = binding;
        }

        return result;
    }
}
