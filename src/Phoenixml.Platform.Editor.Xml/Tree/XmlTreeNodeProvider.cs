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
        _document.Changed += OnDocumentChanged;
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
