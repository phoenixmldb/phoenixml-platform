using System.Collections.Generic;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Abstractions.Editing;

namespace Phoenixml.Platform.Editor.Xml.Tree;

/// <summary>
/// <see cref="ITreeNodeProvider"/> over an <see cref="XmlDocument"/>'s XDM tree.
/// Roots is a single-element list containing the document node;
/// children of an element include its attributes (rendered first) and its child nodes.
/// </summary>
public sealed class XmlTreeNodeProvider : ITreeNodeProvider
{
    private readonly XmlDocument _document;

    /// <summary>
    /// Initializes a new <see cref="XmlTreeNodeProvider"/> for the given document.
    /// </summary>
    /// <param name="document">The XML document whose XDM tree this provider exposes.</param>
    public XmlTreeNodeProvider(XmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        _document.Changed += OnDocumentChanged;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ITreeNode> Roots => [(ITreeNode)new XmlTreeNode(_document, _document.Root, this)];

    /// <inheritdoc/>
    public IReadOnlyList<ITreeNode> GetChildren(ITreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is not XmlTreeNode xmlNode)
            throw new ArgumentException("Node must be an XmlTreeNode.", nameof(node));

        return xmlNode.XdmNode switch
        {
            XdmDocument doc => GetDocumentChildren(doc),
            XdmElement el => GetElementChildren(el),
            _ => Array.Empty<ITreeNode>()
        };
    }

    /// <summary>
    /// Optional diagnostic index. When set, <see cref="XmlTreeNode.IconKey"/> returns
    /// a diagnostic severity key ("diag.error" / "diag.warning") for nodes that have a
    /// recorded diagnostic, overriding the default kind-based icon key. Set this before
    /// (or after) each validation pass; call <see cref="TriggerRefresh"/> to trigger a
    /// NodeListView rebuild after updating.
    /// </summary>
    public IDiagnosticIndex? DiagnosticIndex { get; set; }

    /// <summary>
    /// Optional match index. When set, <see cref="XmlTreeNode.MatchHighlight"/> returns
    /// the current find-match state for each node. Set by the editor's <c>FindController</c>
    /// after each <c>FindService.Find</c> pass; call <see cref="TriggerRefresh"/> to
    /// propagate highlight changes to the tree view.
    /// </summary>
    public IMatchIndex? MatchIndex { get; set; }

    /// <summary>
    /// Optional diff index. When set, <see cref="XmlTreeNode.DiffHighlight"/> returns the
    /// diff status for each node so the dual-pane compare view (Plan 24) can tint rows by
    /// added / removed / changed. Set by <c>CompareDocsWindow</c> before showing.
    /// </summary>
    public Diff.DiffIndex? DiffIndex { get; set; }

    /// <summary>
    /// Optional compiled <see cref="System.Xml.Schema.XmlSchemaSet"/> used to resolve
    /// <c>xs:annotation/xs:documentation</c> for each tree row. Set by
    /// <c>EditorValidationService</c> when a schema is resolved for the active document.
    /// </summary>
    public System.Xml.Schema.XmlSchemaSet? SchemaSet { get; set; }

    /// <summary>
    /// Manually fires the <see cref="Changed"/> event, causing any bound view (e.g.
    /// <c>NodeListView</c>) to rebuild its rows. Call this after updating
    /// <see cref="DiagnosticIndex"/> so IconKey changes propagate to the UI.
    /// </summary>
    public void TriggerRefresh() => Changed?.Invoke();

    /// <inheritdoc/>
    // Raised when the underlying document mutates. Fires on every Document.Changed event
    // so TreeEditorView can rebuild the tree in response to edits.
#pragma warning disable CA1003 // Use generic event handler instances — intentional Action pattern, matches ITreeNodeProvider spec
    public event Action? Changed;
#pragma warning restore CA1003

    private void OnDocumentChanged(Phoenixml.Platform.Abstractions.Documents.DocumentChange change) =>
        Changed?.Invoke();

    private List<ITreeNode> GetDocumentChildren(XdmDocument doc)
    {
        var children = new List<ITreeNode>();
        foreach (var childId in doc.Children)
        {
            var child = _document.Resolve(childId);
            if (child is not null)
                children.Add(new XmlTreeNode(_document, child, this));
        }
        return children;
    }

    private List<ITreeNode> GetElementChildren(XdmElement el)
    {
        var children = new List<ITreeNode>();

        // Always include attributes — they always render as separate rows.
        foreach (var attrId in el.Attributes)
        {
            var attr = _document.Resolve(attrId);
            if (attr is not null)
                children.Add(new XmlTreeNode(_document, attr, this));
        }

        // Determine if any child is an element. Text-only elements (and ones with only
        // comments / PIs) collapse their text content into the Value column — we don't
        // render the text as a separate child row, otherwise the same text appears twice
        // (once in the parent's value column, once as a child row).
        // Mixed content (text interleaved with elements) keeps text-node rows so they're
        // positioned correctly in document order.
        bool hasElementChildren = false;
        foreach (var childId in el.Children)
        {
            if (_document.Resolve(childId) is XdmElement)
            {
                hasElementChildren = true;
                break;
            }
        }

        if (hasElementChildren)
        {
            foreach (var childId in el.Children)
            {
                var child = _document.Resolve(childId);
                if (child is not null)
                    children.Add(new XmlTreeNode(_document, child, this));
            }
        }

        return children;
    }
}
