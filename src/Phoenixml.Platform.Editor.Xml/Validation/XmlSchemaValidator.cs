using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Schema;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
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
            var line = e.Exception?.LineNumber ?? 0;
            var col  = e.Exception?.LinePosition ?? 0;
            var nodeId = line > 0 ? FindNodeNearLine(document, line, col) : null;

            diagnostics.Add(new XmlDiagnostic
            {
                Severity = e.Severity == XmlSeverityType.Warning
                    ? DiagnosticSeverity.Warning
                    : DiagnosticSeverity.Error,
                Code = line > 0 ? $"line {line}, col {col}" : null,
                Message = e.Message,
                Location = nodeId is NodeId id
                    ? new DiagnosticLocation(id, line, col)
                    : null,
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

    /// <summary>
    /// Walks the XDM tree depth-first to find the node whose parse-time source position
    /// is the closest match (greatest <c>SourceLine</c> ≤ <paramref name="line"/>; on a
    /// tie, greatest <c>SourceColumn</c> ≤ <paramref name="column"/>).
    /// </summary>
    /// <param name="document">The document to search.</param>
    /// <param name="line">One-based target line number from the validator.</param>
    /// <param name="column">One-based target column number from the validator.</param>
    /// <returns>The <see cref="NodeId"/> of the best-matching node, or <see langword="null"/>
    /// when no node carries source position information (SourceLine == 0 for all).</returns>
    private static NodeId? FindNodeNearLine(XmlDocument document, int line, int column)
    {
        NodeId? best = null;
        int bestLine = 0;
        int bestCol  = 0;

        void Walk(NodeId id)
        {
            var node = document.Resolve(id);
            if (node is null) return;

            if (node.SourceLine > 0 && node.SourceLine <= line)
            {
                // Prefer the latest start line ≤ target. Tiebreak: prefer the highest
                // column ≤ target on the same line, to land on the innermost open tag.
                bool better = node.SourceLine > bestLine
                    || (node.SourceLine == bestLine
                        && node.SourceColumn <= column
                        && node.SourceColumn > bestCol);

                if (better)
                {
                    best    = id;
                    bestLine = node.SourceLine;
                    bestCol  = node.SourceColumn;
                }
            }

            if (node is XdmElement el)
            {
                foreach (var attrId in el.Attributes) Walk(attrId);
                foreach (var childId in el.Children)  Walk(childId);
            }
            else if (node is XdmDocument d)
            {
                foreach (var childId in d.Children) Walk(childId);
            }
        }

        Walk(document.Root.Id);
        return best;
    }
}
