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

    /// <summary>
    /// Initializes a new <see cref="XmlTreeNode"/> for the given XDM node.
    /// </summary>
    /// <param name="document">The owning XML document.</param>
    /// <param name="node">The XDM node to wrap.</param>
    internal XmlTreeNode(XmlDocument document, XdmNode node)
    {
        _document = document;
        XdmNode = node;
    }

    /// <summary>The wrapped XDM node.</summary>
    public XdmNode XdmNode { get; }

    /// <inheritdoc/>
    public string Id => XdmNode.Id.ToString();

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
    public string? IconKey => XdmNode.NodeKind switch
    {
        XdmNodeKind.Document => "icon.xml.document",
        XdmNodeKind.Element => "icon.xml.element",
        XdmNodeKind.Attribute => "icon.xml.attribute",
        XdmNodeKind.Text => "icon.xml.text",
        XdmNodeKind.Comment => "icon.xml.comment",
        XdmNodeKind.ProcessingInstruction => "icon.xml.pi",
        _ => null
    };

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

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
