using System.Xml;
using System.Xml.Schema;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace Phoenixml.Platform.Editor.Xml.Validation;

/// <summary>
/// Resolves the <see cref="XmlSchemaElement"/> declaration for any in-memory
/// <see cref="XdmElement"/> by walking top-down from the document root.
/// </summary>
/// <remarks>
/// The walk reads from <see cref="XmlSchemaSet.GlobalElements"/> for the root, then
/// descends through <c>xs:sequence</c>, <c>xs:choice</c>, and <c>xs:all</c> particles
/// in each parent's complex type.
/// </remarks>
public static class SchemaElementResolver
{
    /// <summary>
    /// Resolves the <see cref="XmlSchemaElement"/> declaration for the element identified by
    /// <paramref name="targetId"/>.
    /// </summary>
    /// <param name="doc">The document containing the target element.</param>
    /// <param name="targetId">The <see cref="NodeId"/> of the element to resolve.</param>
    /// <param name="schemas">The compiled <see cref="XmlSchemaSet"/> to query.</param>
    /// <returns>
    /// The <see cref="XmlSchemaElement"/> for the target, or <see langword="null"/> when no
    /// schema is bound, the document root is not declared in <paramref name="schemas"/>, or
    /// the path from root to target traverses an element with no declared content model.
    /// </returns>
    public static XmlSchemaElement? Resolve(XmlDocument doc, NodeId targetId, XmlSchemaSet schemas)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(schemas);

        // Build the ancestor chain from the document root down to the target.
        var path = BuildAncestorChain(doc, targetId);
        if (path.Count == 0) return null;

        // Resolve the root element's declaration from GlobalElements.
        var rootElement = path[0];
        var rootNsUri = doc.ResolveNamespace(rootElement.Namespace) ?? string.Empty;
        var rootQName = new XmlQualifiedName(rootElement.LocalName, rootNsUri);
        if (schemas.GlobalElements[rootQName] is not XmlSchemaElement rootSchema)
            return null;

        // Walk down: each step resolves the child element within the parent's content particle.
        var current = rootSchema;
        for (int i = 1; i < path.Count; i++)
        {
            var childElement = path[i];
            var childNsUri = doc.ResolveNamespace(childElement.Namespace) ?? string.Empty;
            var childQName = new XmlQualifiedName(childElement.LocalName, childNsUri);
            current = FindChildElementInParticle(current, childQName);
            if (current is null) return null;
        }

        return current;
    }

    // -------------------------------------------------------------------------
    // Ancestor chain builder.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the chain of <see cref="XdmElement"/> nodes from the document root down to
    /// (and including) the element identified by <paramref name="targetId"/>.
    /// Returns an empty list when the target does not exist or is not an element.
    /// </summary>
    private static List<XdmElement> BuildAncestorChain(XmlDocument doc, NodeId targetId)
    {
        var chain = new List<XdmElement>();
        var node = doc.Resolve(targetId);

        while (node is XdmElement el)
        {
            chain.Insert(0, el);
            node = el.Parent is NodeId parentId ? doc.Resolve(parentId) : null;
        }

        return chain;
    }

    // -------------------------------------------------------------------------
    // Schema particle walker.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Searches the content particle of <paramref name="parent"/> for a directly-named child
    /// element matching <paramref name="childName"/>.
    /// </summary>
    private static XmlSchemaElement? FindChildElementInParticle(
        XmlSchemaElement parent,
        XmlQualifiedName childName)
    {
        if (parent.ElementSchemaType is not XmlSchemaComplexType complex) return null;
        return WalkParticle(complex.ContentTypeParticle, childName);
    }

    /// <summary>
    /// Recursively walks an <see cref="XmlSchemaParticle"/> tree looking for an
    /// <see cref="XmlSchemaElement"/> whose <see cref="XmlSchemaElement.QualifiedName"/>
    /// equals <paramref name="childName"/>.
    /// </summary>
    private static XmlSchemaElement? WalkParticle(
        XmlSchemaParticle? particle,
        XmlQualifiedName childName)
    {
        switch (particle)
        {
            case XmlSchemaElement el when el.QualifiedName == childName:
                return el;

            case XmlSchemaSequence seq:
                foreach (XmlSchemaParticle item in seq.Items)
                    if (WalkParticle(item, childName) is XmlSchemaElement matchSeq)
                        return matchSeq;
                return null;

            case XmlSchemaChoice choice:
                foreach (XmlSchemaParticle item in choice.Items)
                    if (WalkParticle(item, childName) is XmlSchemaElement matchChoice)
                        return matchChoice;
                return null;

            case XmlSchemaAll all:
                foreach (XmlSchemaParticle item in all.Items)
                    if (WalkParticle(item, childName) is XmlSchemaElement matchAll)
                        return matchAll;
                return null;

            default:
                return null;
        }
    }
}
