using System.Collections.Generic;
using System.Xml.Schema;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace Phoenixml.Platform.Editor.Xml.Validation;

/// <summary>
/// The resolved insert options for a parent element returned by
/// <see cref="SchemaInsertOptionsService"/>.
/// </summary>
/// <param name="ChildElementNames">
/// Valid child element local names in XSD declaration order, deduplicated.
/// </param>
/// <param name="AttributeNames">
/// Declared attribute local names that are not already present on the element.
/// </param>
public sealed record SchemaInsertOptions(
    IReadOnlyList<string> ChildElementNames,
    IReadOnlyList<string> AttributeNames);

/// <summary>
/// Given a parent element and the active schema, returns the ordered list of valid child
/// element names and declared attribute names (excluding those already present on the element).
/// </summary>
/// <remarks>
/// Pure function over the schema; no document mutation. Order matches XSD declaration order —
/// sequence, choice, and all particles all flatten into the same list since content-model
/// position is not enforced for v0.10.
/// </remarks>
public static class SchemaInsertOptionsService
{
    private static readonly SchemaInsertOptions Empty = new(
        Array.Empty<string>(),
        Array.Empty<string>());

    /// <summary>
    /// Resolves insert options for the element identified by <paramref name="parentId"/>.
    /// </summary>
    /// <param name="doc">The document containing the parent element.</param>
    /// <param name="parentId">The <see cref="NodeId"/> of the parent element.</param>
    /// <param name="schemas">The compiled <see cref="XmlSchemaSet"/> to query.</param>
    /// <returns>
    /// A <see cref="SchemaInsertOptions"/> record with the valid child element names and
    /// undeclared attribute names. Returns empty lists when the schema is not bound, the
    /// element is not declared, or the element has a simple type (no child elements).
    /// </returns>
    public static SchemaInsertOptions Resolve(XmlDocument doc, NodeId parentId, XmlSchemaSet schemas)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(schemas);

        if (doc.Resolve(parentId) is not XdmElement parent)
            return Empty;

        if (SchemaElementResolver.Resolve(doc, parentId, schemas) is not XmlSchemaElement parentSchema)
            return Empty;

        if (parentSchema.ElementSchemaType is not XmlSchemaComplexType complex)
            return Empty;

        // Collect valid child element names from the content particle.
        var childNames = new List<string>();
        CollectChildElementNames(complex.ContentTypeParticle, childNames);

        // Collect the local names of attributes already present on the element.
        var existingAttrs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attrId in parent.Attributes)
        {
            if (doc.Resolve(attrId) is XdmAttribute attr)
                existingAttrs.Add(attr.LocalName);
        }

        // Collect declared attribute names, excluding those already present.
        var attrNames = new List<string>();
        foreach (XmlSchemaAttribute attr in complex.AttributeUses.Values)
        {
            if (!existingAttrs.Contains(attr.QualifiedName.Name))
                attrNames.Add(attr.QualifiedName.Name);
        }

        return new SchemaInsertOptions(childNames, attrNames);
    }

    // -------------------------------------------------------------------------
    // Content particle walker.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Recursively collects element local names reachable from <paramref name="particle"/>,
    /// appending each unique name to <paramref name="sink"/> in declaration order.
    /// </summary>
    private static void CollectChildElementNames(XmlSchemaParticle? particle, List<string> sink)
    {
        switch (particle)
        {
            case XmlSchemaElement el when !string.IsNullOrEmpty(el.QualifiedName.Name):
                if (!sink.Contains(el.QualifiedName.Name, StringComparer.Ordinal))
                    sink.Add(el.QualifiedName.Name);
                return;

            case XmlSchemaSequence seq:
                foreach (XmlSchemaParticle item in seq.Items)
                    CollectChildElementNames(item, sink);
                return;

            case XmlSchemaChoice choice:
                foreach (XmlSchemaParticle item in choice.Items)
                    CollectChildElementNames(item, sink);
                return;

            case XmlSchemaAll all:
                foreach (XmlSchemaParticle item in all.Items)
                    CollectChildElementNames(item, sink);
                return;
        }
    }
}
