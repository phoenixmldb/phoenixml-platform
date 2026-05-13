using System;
using System.Xml.Schema;

namespace Phoenixml.Platform.Editor.Xml.Validation;

/// <summary>
/// Picks a sensible default text value for a schema-bound type, used to pre-fill
/// schema-aware Insert operations (Plan 25 #5). Returns empty string for unknown
/// types so the user gets a clean editable cell. Enumerations return the first value.
/// </summary>
public static class SchemaDefaultValueResolver
{
    /// <summary>Default text value for <paramref name="type"/>.</summary>
    public static string GetDefault(XmlSchemaType? type)
    {
        if (type is null) return string.Empty;

        if (type is XmlSchemaSimpleType st && st.Content is XmlSchemaSimpleTypeRestriction restr)
        {
            foreach (var facet in restr.Facets)
            {
                if (facet is XmlSchemaEnumerationFacet enumFacet && !string.IsNullOrEmpty(enumFacet.Value))
                    return enumFacet.Value;
            }
        }

        return type.QualifiedName.Name switch
        {
            "string" or "normalizedString" or "token" or "Name" or "NCName" or "language" => string.Empty,
            "integer" or "int" or "long" or "short" or "byte" or "unsignedInt"
                or "unsignedLong" or "unsignedShort" or "unsignedByte"
                or "nonNegativeInteger" or "positiveInteger"
                or "nonPositiveInteger" or "negativeInteger" => "0",
            "decimal" or "double" or "float" => "0.0",
            "boolean" => "false",
            "date" => DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            "dateTime" => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
            "time" => DateTime.UtcNow.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            "anyURI" => string.Empty,
            _ => string.Empty,
        };
    }
}
