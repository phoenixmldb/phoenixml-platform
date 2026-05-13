using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;

namespace Phoenixml.Platform.Editor.Xml.Validation;

/// <summary>
/// Harvests element + attribute local names from an <see cref="XmlSchemaSet"/> for use
/// as XPath bar completion candidates. Returns globals + every element/attribute reachable
/// through composed content models. Sorted ordinal, deduplicated.
/// </summary>
public static class SchemaElementNameHarvester
{
    /// <summary>All reachable element local names in <paramref name="schemas"/>.</summary>
    public static IReadOnlyList<string> AllReachableElementNames(XmlSchemaSet schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        if (!schemas.IsCompiled) schemas.Compile();

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (XmlSchemaElement element in schemas.GlobalElements.Values)
        {
            CollectElementNames(element, set, new HashSet<XmlSchemaObject>());
        }
        var list = new List<string>(set);
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    /// <summary>All reachable attribute local names in <paramref name="schemas"/>.</summary>
    public static IReadOnlyList<string> AllAttributeNames(XmlSchemaSet schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        if (!schemas.IsCompiled) schemas.Compile();

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (XmlSchemaAttribute attr in schemas.GlobalAttributes.Values)
        {
            if (!string.IsNullOrEmpty(attr.QualifiedName.Name))
                set.Add(attr.QualifiedName.Name);
        }
        // Element-local attributes
        foreach (XmlSchemaElement element in schemas.GlobalElements.Values)
        {
            CollectAttributeNames(element, set, new HashSet<XmlSchemaObject>());
        }
        var list = new List<string>(set);
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static void CollectElementNames(XmlSchemaElement element, HashSet<string> set, HashSet<XmlSchemaObject> visited)
    {
        if (!visited.Add(element)) return;
        if (!string.IsNullOrEmpty(element.QualifiedName.Name))
            set.Add(element.QualifiedName.Name);

        // Use SchemaType (the original definition) preferentially; fall back to ElementSchemaType.
        var type = element.SchemaType ?? element.ElementSchemaType;
        if (type is XmlSchemaComplexType ct)
            CollectFromComplexType(ct, set, visited);
    }

    private static void CollectFromComplexType(XmlSchemaComplexType ct, HashSet<string> set, HashSet<XmlSchemaObject> visited)
    {
        if (!visited.Add(ct)) return;
        // Walk both the original Particle (set when type uses xs:sequence/xs:choice/xs:all)
        // and the compiled ContentTypeParticle. They overlap for simple cases but each can
        // be empty in certain compositions.
        if (ct.Particle is XmlSchemaParticle p1) CollectFromParticle(p1, set, visited);
        if (ct.ContentTypeParticle is XmlSchemaParticle p2) CollectFromParticle(p2, set, visited);
    }

    private static void CollectFromParticle(XmlSchemaParticle particle, HashSet<string> set, HashSet<XmlSchemaObject> visited)
    {
        switch (particle)
        {
            case XmlSchemaElement el:
                CollectElementNames(el, set, visited);
                break;
            case XmlSchemaGroupBase group:
                foreach (XmlSchemaParticle p in group.Items)
                    CollectFromParticle(p, set, visited);
                break;
        }
    }

    private static void CollectAttributeNames(XmlSchemaElement element, HashSet<string> set, HashSet<XmlSchemaObject> visited)
    {
        if (!visited.Add(element)) return;
        var type = element.SchemaType ?? element.ElementSchemaType;
        if (type is XmlSchemaComplexType ct)
        {
            foreach (var entry in ct.AttributeUses.Values)
            {
                if (entry is XmlSchemaAttribute attr && !string.IsNullOrEmpty(attr.QualifiedName.Name))
                    set.Add(attr.QualifiedName.Name);
            }
            if (ct.Particle is XmlSchemaParticle p1) CollectAttributesFromParticle(p1, set, visited);
            if (ct.ContentTypeParticle is XmlSchemaParticle p2) CollectAttributesFromParticle(p2, set, visited);
        }
    }

    private static void CollectAttributesFromParticle(XmlSchemaParticle particle, HashSet<string> set, HashSet<XmlSchemaObject> visited)
    {
        switch (particle)
        {
            case XmlSchemaElement el:
                CollectAttributeNames(el, set, visited);
                break;
            case XmlSchemaGroupBase group:
                foreach (XmlSchemaParticle p in group.Items)
                    CollectAttributesFromParticle(p, set, visited);
                break;
        }
    }
}
