using System.Collections.Generic;
using System.Text;
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
/// <remarks>
/// Validation is push-based: the validator walks the in-memory <see cref="XdmDocument"/> tree
/// and drives <see cref="System.Xml.Schema.XmlSchemaValidator"/> with explicit
/// <c>ValidateElement</c> / <c>ValidateAttribute</c> / <c>ValidateText</c> /
/// <c>ValidateEndElement</c> calls. The current <see cref="XdmNode"/> is in scope when each
/// call returns, so diagnostics attach exact source positions without any serialization
/// round-trip or line/column approximation.
/// </remarks>
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
        var nameTable = new NameTable();
        var nsManager = new XmlNamespaceManager(nameTable);

        var validator = new System.Xml.Schema.XmlSchemaValidator(
            nameTable,
            schemas,
            nsManager,
            XmlSchemaValidationFlags.ReportValidationWarnings
                | XmlSchemaValidationFlags.ProcessIdentityConstraints
                | XmlSchemaValidationFlags.ProcessSchemaLocation
                | XmlSchemaValidationFlags.ProcessInlineSchema);

        // Mutable context object shared with the event handler so the lambda can read
        // whichever node is active at the moment the validation event fires.
        var ctx = new ValidationContext();

        validator.ValidationEventHandler += (_, e) =>
        {
            var node = ctx.CurrentNode;
            DiagnosticLocation? loc = null;
            string? code = null;
            if (node is not null && node.SourceLine > 0)
            {
                loc = new DiagnosticLocation(node.Id, node.SourceLine, node.SourceColumn);
                code = $"line {node.SourceLine}, col {node.SourceColumn}";
            }

            diagnostics.Add(new XmlDiagnostic
            {
                Severity = e.Severity == XmlSeverityType.Warning
                    ? DiagnosticSeverity.Warning
                    : DiagnosticSeverity.Error,
                Code = code,
                Message = e.Message,
                Location = loc,
            });
        };

        validator.Initialize();
        WalkDocument(document, validator, nsManager, ctx);
        validator.EndValidation();
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

    // -------------------------------------------------------------------------
    // Mutable validation context — shared by reference with the event handler.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Tracks the <see cref="XdmNode"/> currently being processed so the
    /// <see cref="System.Xml.Schema.XmlSchemaValidator"/> event handler can attach
    /// diagnostics to the exact source node.
    /// </summary>
    private sealed class ValidationContext
    {
        /// <summary>The node active at the time the next validation event may fire.</summary>
        public XdmNode? CurrentNode { get; set; }
    }

    // -------------------------------------------------------------------------
    // Tree walk.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Walks the document root's top-level children and drives the validator for each
    /// top-level element. Comments, PIs, and other non-element children at document level
    /// are skipped — XSD validation applies only to element content.
    /// </summary>
    private static void WalkDocument(
        XmlDocument doc,
        System.Xml.Schema.XmlSchemaValidator validator,
        XmlNamespaceManager nsManager,
        ValidationContext ctx)
    {
        foreach (var childId in doc.Root.Children)
        {
            if (doc.Resolve(childId) is XdmElement el)
                WalkElement(doc, el, validator, nsManager, ctx);
        }
    }

    /// <summary>
    /// Recursively drives the validator for <paramref name="element"/> and all its
    /// descendants, maintaining namespace scope throughout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Namespace declarations on the element are pushed to <paramref name="nsManager"/>
    /// before calling <c>ValidateElement</c> so the validator can resolve namespace prefixes
    /// in attribute values and schema-location hints.
    /// </para>
    /// <para>
    /// Text-content diagnostics are attached to the containing element (not the text child
    /// node) so the inline glyph renders on the element row in the editor.
    /// </para>
    /// </remarks>
    private static void WalkElement(
        XmlDocument doc,
        XdmElement element,
        System.Xml.Schema.XmlSchemaValidator validator,
        XmlNamespaceManager nsManager,
        ValidationContext ctx)
    {
        ctx.CurrentNode = element;

        nsManager.PushScope();

        // Push this element's xmlns declarations into the manager BEFORE calling
        // ValidateElement so the validator can resolve namespace prefixes.
        // NamespaceBinding.Namespace is a NamespaceId; resolve to URI string via doc.
        foreach (var binding in element.NamespaceDeclarations)
        {
            var uri = doc.ResolveNamespace(binding.Namespace) ?? string.Empty;
            nsManager.AddNamespace(binding.Prefix ?? string.Empty, uri);
        }

        // Resolve the element's own namespace URI.
        var elNsUri = doc.ResolveNamespace(element.Namespace) ?? string.Empty;

        validator.ValidateElement(
            localName: element.LocalName,
            namespaceUri: elNsUri,
            schemaInfo: null);

        // Validate attributes. XdmElement.Attributes does NOT include xmlns declarations
        // (those live in NamespaceDeclarations), so no xmlns filtering is needed here.
        foreach (var attrId in element.Attributes)
        {
            if (doc.Resolve(attrId) is not XdmAttribute attr) continue;

            ctx.CurrentNode = attr;
            var attrNsUri = doc.ResolveNamespace(attr.Namespace) ?? string.Empty;

            validator.ValidateAttribute(
                localName: attr.LocalName,
                namespaceUri: attrNsUri,
                attributeValue: attr.Value,
                schemaInfo: null);
        }

        // ValidateEndOfAttributes is required even when there are no attributes.
        ctx.CurrentNode = element;
        validator.ValidateEndOfAttributes(schemaInfo: null);

        // Walk children: buffer adjacent text runs, flush before descending into
        // child elements. Text-content diagnostics attach to the parent element so
        // the editor glyph lands on the element row, not an invisible text node.
        var textBuffer = new StringBuilder();

        foreach (var childId in element.Children)
        {
            var child = doc.Resolve(childId);

            if (child is XdmText t)
            {
                textBuffer.Append(t.Value);
                // Do NOT change ctx.CurrentNode here — keep it pointing at the
                // element so any text-content diagnostic attaches to the element.
            }
            else if (child is XdmElement childEl)
            {
                // Flush any accumulated text before descending.
                if (textBuffer.Length > 0)
                {
                    ctx.CurrentNode = element;
                    validator.ValidateText(textBuffer.ToString());
                    textBuffer.Clear();
                }

                WalkElement(doc, childEl, validator, nsManager, ctx);
                ctx.CurrentNode = element;
            }
            // Comments and PIs are transparent to XSD validation — skip them.
        }

        // Flush trailing text (typical for simple-content elements: <year>1978</year>).
        if (textBuffer.Length > 0)
        {
            ctx.CurrentNode = element;
            validator.ValidateText(textBuffer.ToString());
            textBuffer.Clear();
        }

        ctx.CurrentNode = element;
        validator.ValidateEndElement(schemaInfo: null);

        nsManager.PopScope();
    }
}
