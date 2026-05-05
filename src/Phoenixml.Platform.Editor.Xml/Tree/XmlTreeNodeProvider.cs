using System.Collections.Generic;
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
    }

    /// <inheritdoc/>
    public IReadOnlyList<ITreeNode> Roots => [(ITreeNode)new XmlTreeNode(_document, _document.Root)];

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

    /// <inheritdoc/>
    // Raised when the underlying document mutates. Mutation isn't wired in v0.1.0 so
    // Changed never fires; consumers can subscribe today and benefit when edits land later.
#pragma warning disable CA1003 // Use generic event handler instances — intentional Action pattern, matches ITreeNodeProvider spec
#pragma warning disable CS0067  // Event never used — interface requirement; fires when mutation lands in a later plan
    public event Action? Changed;
#pragma warning restore CS0067
#pragma warning restore CA1003

    private List<ITreeNode> GetDocumentChildren(XdmDocument doc)
    {
        var children = new List<ITreeNode>();
        foreach (var childId in doc.Children)
        {
            var child = _document.Resolve(childId);
            if (child is not null)
                children.Add(new XmlTreeNode(_document, child));
        }
        return children;
    }

    private List<ITreeNode> GetElementChildren(XdmElement el)
    {
        var children = new List<ITreeNode>();
        foreach (var attrId in el.Attributes)
        {
            var attr = _document.Resolve(attrId);
            if (attr is not null)
                children.Add(new XmlTreeNode(_document, attr));
        }
        foreach (var childId in el.Children)
        {
            var child = _document.Resolve(childId);
            if (child is not null)
                children.Add(new XmlTreeNode(_document, child));
        }
        return children;
    }
}
