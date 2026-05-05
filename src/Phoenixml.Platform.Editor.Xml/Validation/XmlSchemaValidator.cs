using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Schema;
using Phoenixml.Platform.Abstractions.Editing;

namespace Phoenixml.Platform.Editor.Xml.Validation;

/// <summary>
/// Validates an <see cref="XmlDocument"/> against an XSD schema set. Surfaces violations
/// as <see cref="XmlDiagnostic"/> for binding to ProblemsView.
/// </summary>
public sealed class XmlSchemaValidator
{
    /// <summary>
    /// Validates <paramref name="document"/> against <paramref name="schemas"/>.
    /// </summary>
    /// <param name="document">The XML document to validate.</param>
    /// <param name="schemas">The compiled XSD schema set to validate against.</param>
    /// <returns>A read-only list of diagnostics; empty if the document is valid.</returns>
    public static IReadOnlyList<IDiagnostic> Validate(XmlDocument document, XmlSchemaSet schemas)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(schemas);

        var diagnostics = new List<IDiagnostic>();
        var xml = document.SerializeToString();

        var settings = new XmlReaderSettings
        {
            Schemas = schemas,
            ValidationType = ValidationType.Schema,
            ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings
                            | XmlSchemaValidationFlags.ProcessIdentityConstraints,
        };

        settings.ValidationEventHandler += (_, e) =>
        {
            diagnostics.Add(new XmlDiagnostic
            {
                Severity = e.Severity == XmlSeverityType.Warning
                    ? DiagnosticSeverity.Warning
                    : DiagnosticSeverity.Error,
                Code = e.Exception?.LineNumber > 0
                    ? $"line {e.Exception.LineNumber}, col {e.Exception.LinePosition}"
                    : null,
                Message = e.Message,
                Location = null,  // TODO: map line/col back to XdmNode in a later plan
            });
        };

        using var reader = XmlReader.Create(new StringReader(xml), settings);
        try
        {
            while (reader.Read()) { /* drain */ }
        }
        catch (XmlException xex)
        {
            diagnostics.Add(new XmlDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = $"line {xex.LineNumber}, col {xex.LinePosition}",
                Message = xex.Message,
            });
        }

        return diagnostics;
    }

    /// <summary>Loads an XSD schema from a file path and returns a compiled schema set.</summary>
    /// <param name="xsdPath">Absolute path to the XSD file.</param>
    /// <returns>A compiled <see cref="XmlSchemaSet"/>.</returns>
    public static XmlSchemaSet LoadSchema(string xsdPath)
    {
        ArgumentNullException.ThrowIfNull(xsdPath);
        var schemas = new XmlSchemaSet();
        schemas.Add(targetNamespace: null, xsdPath);
        schemas.Compile();
        return schemas;
    }
}
