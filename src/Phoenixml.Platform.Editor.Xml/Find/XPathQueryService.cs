using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Parser;

namespace Phoenixml.Platform.Editor.Xml.Find;

/// <summary>
/// Result of an XPath evaluation: the matching <see cref="NodeId"/>s in document order,
/// or a compile/runtime error message.
/// </summary>
/// <param name="Nodes">Matching nodes (empty when <paramref name="Error"/> is non-null).</param>
/// <param name="Error">Diagnostic message, or <see langword="null"/> on success.</param>
public sealed record XPathResult(IReadOnlyList<NodeId> Nodes, string? Error);

/// <summary>
/// Evaluates an XPath expression against an <see cref="XmlDocument"/> using PhoenixmlDb.XQuery.
/// Wraps the engine so the editor surface trades only in <see cref="NodeId"/>s.
/// </summary>
/// <remarks>
/// XPath is a subset of XQuery; the expression is fed verbatim to <see cref="QueryEngine"/>.
/// Compile-time and runtime errors are caught and returned as <see cref="XPathResult.Error"/> so
/// the UI can iterate on syntax without exceptions propagating.
/// </remarks>
public static class XPathQueryService
{
    /// <summary>
    /// Compiles and evaluates <paramref name="xpath"/> against <paramref name="doc"/>'s root.
    /// </summary>
    public static async Task<XPathResult> EvaluateAsync(
        XmlDocument doc,
        string xpath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (string.IsNullOrWhiteSpace(xpath))
            return new XPathResult(Array.Empty<NodeId>(), null);

        var store = new XmlDocumentNodeStore(doc);
        var engine = new QueryEngine(nodeProvider: store);

        QueryCompilationResult compile;
        try
        {
            compile = engine.Compile(xpath);
        }
        catch (XQueryParseException ex)
        {
            return new XPathResult(Array.Empty<NodeId>(), ex.Message);
        }

        if (!compile.Success || compile.ExecutionPlan is null)
        {
            var msg = compile.Errors is { Count: > 0 }
                ? string.Join("; ", compile.Errors.Select(e => e.Message))
                : "XPath compilation failed.";
            return new XPathResult(Array.Empty<NodeId>(), msg);
        }

        var matches = new List<NodeId>();
        try
        {
            await foreach (var item in engine.ExecuteAsync(
                compile.ExecutionPlan,
                container: default,
                initialContextItem: doc.Root,
                cancellationToken).ConfigureAwait(false))
            {
                if (item is XdmNode node)
                    matches.Add(node.Id);
            }
        }
        catch (XQueryRuntimeException ex)
        {
            return new XPathResult(Array.Empty<NodeId>(), ex.Message);
        }

        return new XPathResult(matches, null);
    }

    /// <summary>
    /// <see cref="INodeStore"/> adapter over an editor <see cref="XmlDocument"/>.
    /// Bridges the engine's id-based node + namespace lookups to the editor's in-memory tree.
    /// </summary>
    private sealed class XmlDocumentNodeStore : INodeStore
    {
        private readonly XmlDocument _doc;

        public XmlDocumentNodeStore(XmlDocument doc) => _doc = doc;

        public XdmNode? GetNode(NodeId nodeId) => _doc.Resolve(nodeId);

        public string? GetNamespaceUri(NamespaceId id) => _doc.ResolveNamespace(id);

        public IEnumerable<XdmAttribute> GetAttributes(XdmElement element)
        {
            foreach (var attrId in element.Attributes)
            {
                if (_doc.Resolve(attrId) is XdmAttribute attr)
                    yield return attr;
            }
        }
    }
}
