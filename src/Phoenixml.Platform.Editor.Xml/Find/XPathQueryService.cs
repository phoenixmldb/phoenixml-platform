using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
/// a formatted scalar string when the expression yielded only atomics, or an error.
/// </summary>
/// <param name="Nodes">Matching nodes (empty when the result was scalar or errored).</param>
/// <param name="Scalar">
/// Comma-joined, length-capped serialization of atomic items when the expression yielded
/// no nodes (e.g. <c>count(//book)</c> → <c>"12"</c>; <c>1 to 5</c> → <c>"(1, 2, 3, 4, 5)"</c>).
/// <see langword="null"/> when the result contained nodes, was empty, or errored.
/// </param>
/// <param name="Error">Diagnostic message, or <see langword="null"/> on success.</param>
public sealed record XPathResult(
    IReadOnlyList<NodeId> Nodes,
    string? Scalar,
    string? Error);

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
    private const int MaxScalarLength = 80;

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
            return new XPathResult(Array.Empty<NodeId>(), Scalar: null, Error: null);

        var store = new XmlDocumentNodeStore(doc);
        var engine = new QueryEngine(nodeProvider: store);

        QueryCompilationResult compile;
        try
        {
            compile = engine.Compile(xpath);
        }
        catch (XQueryParseException ex)
        {
            return new XPathResult(Array.Empty<NodeId>(), Scalar: null, Error: ex.Message);
        }

        if (!compile.Success || compile.ExecutionPlan is null)
        {
            var msg = compile.Errors is { Count: > 0 }
                ? string.Join("; ", compile.Errors.Select(e => e.Message))
                : "XPath compilation failed.";
            return new XPathResult(Array.Empty<NodeId>(), Scalar: null, Error: msg);
        }

        var matches = new List<NodeId>();
        var atomics = new List<object?>();
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
                else if (item is not null)
                    atomics.Add(item);
            }
        }
        catch (XQueryRuntimeException ex)
        {
            return new XPathResult(Array.Empty<NodeId>(), Scalar: null, Error: ex.Message);
        }

        // Node-set wins when present — tinting is more useful than text. Atomics surface
        // only when there are no nodes.
        var scalar = matches.Count == 0 && atomics.Count > 0
            ? FormatAtomics(atomics)
            : null;

        return new XPathResult(matches, scalar, Error: null);
    }

    /// <summary>
    /// Formats a list of atomic XQuery result items into a single status-bar string.
    /// One item → bare value (<c>"12"</c>); multiple → parenthesized comma list
    /// (<c>"(1, 2, 3, 4, 5)"</c>); truncated at <see cref="MaxScalarLength"/>
    /// with <c>"… +N more"</c> suffix.
    /// </summary>
    internal static string FormatAtomics(IReadOnlyList<object?> atomics)
    {
        if (atomics.Count == 1)
            return FormatAtomic(atomics[0]);

        var sb = new StringBuilder("(");
        var rendered = 0;
        for (int i = 0; i < atomics.Count; i++)
        {
            var s = FormatAtomic(atomics[i]);
            // +2 for ", " separator on subsequent items, +1 for closing ')'.
            var pending = sb.Length + s.Length + (rendered > 0 ? 2 : 0) + 1;
            if (pending > MaxScalarLength && rendered > 0)
            {
                sb.Append(", … +").Append(atomics.Count - rendered).Append(" more");
                break;
            }
            if (rendered > 0) sb.Append(", ");
            sb.Append(s);
            rendered++;
        }
        sb.Append(')');
        return sb.ToString();
    }

    private static string FormatAtomic(object? item)
    {
        if (item is null) return "()";
        if (item is bool b) return b ? "true" : "false";
        // Invariant culture — XQuery atomics serialize without locale formatting.
        return Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty;
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
