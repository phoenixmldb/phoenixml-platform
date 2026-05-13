using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Abstractions.Editing;

namespace Phoenixml.Platform.Editor.Xml.Tree;

/// <summary>
/// <see cref="ITreeNode"/> adapter for an XDM node. Wraps the XdmNode and exposes its
/// kind-appropriate display label.
/// </summary>
public sealed class XmlTreeNode : ITreeNode
{
    private readonly XmlDocument _document;
    private readonly XmlTreeNodeProvider? _provider;

    /// <summary>
    /// Initializes a new <see cref="XmlTreeNode"/> for the given XDM node.
    /// </summary>
    /// <param name="document">The owning XML document.</param>
    /// <param name="node">The XDM node to wrap.</param>
    /// <param name="provider">
    /// The provider that created this node. Used to read the current
    /// <see cref="XmlTreeNodeProvider.DiagnosticIndex"/> when computing
    /// <see cref="IconKey"/>. May be <see langword="null"/> in test contexts.
    /// </param>
    internal XmlTreeNode(XmlDocument document, XdmNode node, XmlTreeNodeProvider? provider = null)
    {
        _document = document;
        XdmNode = node;
        _provider = provider;
    }

    /// <summary>The wrapped XDM node.</summary>
    public XdmNode XdmNode { get; }

    /// <inheritdoc/>
    public string Id => XdmNode.Id.ToString();

    /// <inheritdoc/>
    public string EditableName => XdmNode switch
    {
        XdmElement el => el.LocalName,
        XdmAttribute a => a.LocalName,
        _ => Label
    };

    /// <inheritdoc/>
    public string Label => XdmNode switch
    {
        XdmDocument doc => DocumentLabel(doc),
        XdmElement el => el.LocalName,
        XdmAttribute a => $"@{a.LocalName}",
        XdmText t => $"\"{Truncate(t.Value, 40)}\"",
        XdmComment c => $"<!-- {Truncate(c.Value, 40)} -->",
        XdmProcessingInstruction pi => $"<?{pi.Target} ... ?>",
        _ => XdmNode.NodeKind.ToString()
    };

    private static string DocumentLabel(XdmDocument doc)
    {
        // Show just the filename, not the full file:// URI — full URIs cause horizontal
        // overflow in narrow tree panes. The owning document's DisplayName would be
        // ideal, but XdmDocument doesn't carry it; fall back to the URI's last segment.
        if (string.IsNullOrEmpty(doc.DocumentUri)) return "(document)";
        if (Uri.TryCreate(doc.DocumentUri, UriKind.Absolute, out var u))
        {
            var name = System.IO.Path.GetFileName(u.LocalPath);
            if (!string.IsNullOrEmpty(name)) return name;
        }
        return doc.DocumentUri;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// When a <see cref="XmlTreeNodeProvider.DiagnosticIndex"/> is set on the owning
    /// provider and this node has a recorded diagnostic, returns <c>"diag.error"</c> or
    /// <c>"diag.warning"</c> instead of the normal kind-based key. This is consumed by
    /// <c>NodeListView</c>'s <c>DiagnosticGlyph</c> computed property to render ✕/⚠
    /// inline overlays.
    /// </remarks>
    public string? IconKey
    {
        get
        {
            if (_provider?.DiagnosticIndex?.GetSeverity(XdmNode.Id) is DiagnosticSeverity sev)
            {
                return sev == DiagnosticSeverity.Error ? "diag.error" : "diag.warning";
            }

            return XdmNode.NodeKind switch
            {
                XdmNodeKind.Document => "icon.xml.document",
                XdmNodeKind.Element => "icon.xml.element",
                XdmNodeKind.Attribute => "icon.xml.attribute",
                XdmNodeKind.Text => "icon.xml.text",
                XdmNodeKind.Comment => "icon.xml.comment",
                XdmNodeKind.ProcessingInstruction => "icon.xml.pi",
                _ => null
            };
        }
    }

    /// <inheritdoc/>
    public bool HasChildren => XdmNode switch
    {
        XdmDocument doc => doc.DocumentElement is not null,
        // An element has expandable children if it has attributes OR at least one element
        // child. Text-only leaves (no attrs, no element children, just text) report no
        // children — their text is shown in the Value column instead, matching XmlNotepad's
        // ergonomic of "leaf elements look like attributes" in the dual-pane layout.
        XdmElement el => el.Attributes.Count > 0 || HasElementChildren(el),
        _ => false
    };

    private bool HasElementChildren(XdmElement el)
    {
        foreach (var childId in el.Children)
        {
            if (_document.Resolve(childId) is XdmElement) return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public string Value => XdmNode switch
    {
        XdmDocument => string.Empty,
        XdmElement el => ElementValue(el),
        XdmAttribute a => a.Value,
        XdmText t => t.Value,
        XdmComment c => c.Value,
        XdmProcessingInstruction pi => pi.Value,
        _ => string.Empty
    };

    private string ElementValue(XdmElement el)
    {
        // Resolve each child NodeId to check whether any is an element.
        // Container element (has element children) → empty value column.
        foreach (var childId in el.Children)
        {
            if (_document.Resolve(childId) is XdmElement)
                return string.Empty;
        }

        // Text-only element → concatenate all text node values.
        var sb = new System.Text.StringBuilder();
        foreach (var childId in el.Children)
        {
            if (_document.Resolve(childId) is XdmText t)
                sb.Append(t.Value);
        }
        return sb.ToString();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// When <see cref="XmlTreeNodeProvider.MatchIndex"/> is set on the owning provider,
    /// returns <see cref="MatchHighlight.ActiveMatch"/> for the currently navigated match
    /// and <see cref="MatchHighlight.Match"/> for all other matches in the current result
    /// set. Returns <see cref="MatchHighlight.None"/> when no find operation is active or
    /// this node is not a match.
    /// </remarks>
    public MatchHighlight MatchHighlight
    {
        get
        {
            if (_provider?.MatchIndex is not IMatchIndex idx) return MatchHighlight.None;
            if (idx.IsActive(XdmNode.Id)) return MatchHighlight.ActiveMatch;
            return idx.IsMatch(XdmNode.Id) ? MatchHighlight.Match : MatchHighlight.None;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the diff kind for this node when the owning provider's
    /// <see cref="XmlTreeNodeProvider.DiffIndex"/> is set; otherwise
    /// <see cref="DiffHighlight.None"/>. Compare view sets the index before rendering.
    /// </remarks>
    public DiffHighlight DiffHighlight
    {
        get
        {
            if (_provider?.DiffIndex is not Diff.DiffIndex idx) return DiffHighlight.None;
            return idx.GetKind(XdmNode.Id) switch
            {
                Diff.DiffKind.Added => DiffHighlight.Added,
                Diff.DiffKind.Removed => DiffHighlight.Removed,
                Diff.DiffKind.Changed => DiffHighlight.Changed,
                _ => DiffHighlight.None,
            };
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
