using System.Collections.Immutable;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Abstractions.Documents;
using PlatformDocument = Phoenixml.Platform.Abstractions.Documents.IDocument;
using PlatformEditorCommand = Phoenixml.Platform.Abstractions.Documents.IEditorCommand;

namespace Phoenixml.Platform.Editor.Xml.Editing;

/// <summary>
/// Removes a node (element, attribute, text, comment, or PI) from its parent. The node and all
/// of its descendants stay in <c>_byId</c> so <see cref="Revert"/> can re-link without rebuilding.
/// Compact-on-Save evicts the orphaned subtree once this command leaves both the undo and redo stacks.
/// </summary>
public sealed class DeleteNodeCommand : PlatformEditorCommand
{
    private readonly XmlDocument _document;
    private readonly NodeId _targetId;

    private NodeId _parentId;
    private XdmElement? _previousParent;

    // Populated during Apply; used by ReferencedNodeIds to keep the entire deleted subtree
    // alive in _byId while this command is on either the undo or the redo stack.
    private readonly List<NodeId> _subtreeIds = new();

    /// <summary>
    /// Initializes a new <see cref="DeleteNodeCommand"/>.
    /// </summary>
    /// <param name="document">The document whose node store will be mutated.</param>
    /// <param name="targetId">The <see cref="NodeId"/> of the node to delete.</param>
    public DeleteNodeCommand(XmlDocument document, NodeId targetId)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        _targetId = targetId;
    }

    /// <inheritdoc/>
    public string Description => "Delete node";

    /// <inheritdoc/>
    /// <remarks>
    /// Returns all NodeIds in the deleted subtree (populated during Apply) so that
    /// Compact-on-Save does not evict them while undo is possible.
    /// </remarks>
    public IEnumerable<NodeId> ReferencedNodeIds => _subtreeIds;

    /// <inheritdoc/>
    public void Apply(PlatformDocument document)
    {
        var target = _document.Resolve(_targetId)
            ?? throw new InvalidOperationException($"Node {_targetId} not found.");

        _parentId = target.Parent
            ?? throw new InvalidOperationException("Cannot delete the document root.");

        var parent = _document.Resolve(_parentId) as XdmElement
            ?? throw new InvalidOperationException(
                $"Parent {_parentId} is not an XdmElement; cannot delete node.");

        _previousParent = parent;

        // Collect the full subtree for ReferencedNodeIds. Clear first so re-Apply (redo)
        // rebuilds cleanly (subtree hasn't changed, but safe to redo the walk).
        _subtreeIds.Clear();
        CollectSubtree(target, _subtreeIds);

        XdmElement updatedParent;
        if (target is XdmAttribute)
        {
            updatedParent = WithAttributes(parent, RemoveOnce(parent.Attributes, _targetId));
        }
        else
        {
            updatedParent = WithChildren(parent, RemoveOnce(parent.Children, _targetId));
        }

        _document.ReplaceNode(_parentId, updatedParent);
    }

    /// <inheritdoc/>
    public void Revert(PlatformDocument document)
    {
        if (_previousParent is null) return;
        // Restore the parent with the deleted node back in place.
        // All nodes in the deleted subtree are still in _byId, so re-linking is trivial.
        _document.ReplaceNode(_parentId, _previousParent);
    }

    /// <inheritdoc/>
    public bool TryMergeWith(PlatformEditorCommand previous, out PlatformEditorCommand merged)
    {
        // Deletions never merge.
        merged = this;
        return false;
    }

    private void CollectSubtree(XdmNode node, List<NodeId> ids)
    {
        ids.Add(node.Id);
        if (node is XdmElement el)
        {
            foreach (var attrId in el.Attributes)
            {
                if (_document.Resolve(attrId) is XdmNode attr)
                    ids.Add(attr.Id);
            }
            foreach (var childId in el.Children)
            {
                if (_document.Resolve(childId) is XdmNode child)
                    CollectSubtree(child, ids);
            }
        }
    }

    private static IReadOnlyList<NodeId> RemoveOnce(IReadOnlyList<NodeId> source, NodeId target)
    {
        var idx = -1;
        for (var i = 0; i < source.Count; i++)
        {
            if (source[i] == target)
            {
                idx = i;
                break;
            }
        }
        if (idx < 0) return source;

        var builder = ImmutableArray.CreateBuilder<NodeId>(source.Count - 1);
        for (var i = 0; i < source.Count; i++)
        {
            if (i != idx) builder.Add(source[i]);
        }
        return builder.ToImmutable();
    }

    private static XdmElement WithChildren(XdmElement source, IReadOnlyList<NodeId> newChildren) =>
        new()
        {
            Id = source.Id,
            Document = source.Document,
            Namespace = source.Namespace,
            Prefix = source.Prefix,
            LocalName = source.LocalName,
            Parent = source.Parent,
            Attributes = source.Attributes,
            Children = newChildren,
            NamespaceDeclarations = source.NamespaceDeclarations,
            TypeAnnotation = source.TypeAnnotation,
            IsIdContent = source.IsIdContent,
        };

    private static XdmElement WithAttributes(XdmElement source, IReadOnlyList<NodeId> newAttributes) =>
        new()
        {
            Id = source.Id,
            Document = source.Document,
            Namespace = source.Namespace,
            Prefix = source.Prefix,
            LocalName = source.LocalName,
            Parent = source.Parent,
            Attributes = newAttributes,
            Children = source.Children,
            NamespaceDeclarations = source.NamespaceDeclarations,
            TypeAnnotation = source.TypeAnnotation,
            IsIdContent = source.IsIdContent,
        };
}
