using System;
using System.Text;
using System.Xml;
using System.Xml.Schema;

namespace Phoenixml.Platform.Editor.Xml.Validation;

/// <summary>
/// Extracts <c>xs:documentation</c> text from <c>xs:annotation</c> nodes attached to
/// schema components. Used to populate tooltips for tree rows (Plan 25 #3).
/// </summary>
public static class SchemaAnnotationLookup
{
    /// <summary>Returns documentation text for <paramref name="element"/>, or null.</summary>
    public static string? GetDocumentation(XmlSchemaElement? element)
    {
        if (element is null) return null;
        return Extract(element.Annotation);
    }

    /// <summary>Returns documentation text for <paramref name="attribute"/>, or null.</summary>
    public static string? GetDocumentation(XmlSchemaAttribute? attribute)
    {
        if (attribute is null) return null;
        return Extract(attribute.Annotation);
    }

    private static string? Extract(XmlSchemaAnnotation? annotation)
    {
        if (annotation is null) return null;
        var sb = new StringBuilder();
        foreach (var item in annotation.Items)
        {
            if (item is XmlSchemaDocumentation doc && doc.Markup is { } markup)
            {
                foreach (XmlNode? m in markup)
                {
                    if (m is null) continue;
                    var t = m.InnerText?.Trim();
                    if (!string.IsNullOrEmpty(t))
                    {
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(t);
                    }
                }
            }
        }
        var result = sb.ToString().Trim();
        return result.Length == 0 ? null : result;
    }
}
