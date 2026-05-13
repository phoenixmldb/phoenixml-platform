using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Abstractions.Documents;
using PlatformDocument = Phoenixml.Platform.Abstractions.Documents.IDocument;
using PlatformEditorCommand = Phoenixml.Platform.Abstractions.Documents.IEditorCommand;

namespace Phoenixml.Platform.Editor.Xml.Editing;

/// <summary>
/// Parses an XML string and inserts the resulting subtree as the last child of a target
/// parent element. On <see cref="Apply"/>, the XML is parsed, fresh <see cref="NodeId"/>
/// values are allocated from the target document's allocator, and the subtree root is
/// appended to the parent's children list.
/// </summary>
/// <remarks>
/// <para>
/// <b>Undo/redo:</b> <see cref="Revert"/> restores the parent's pre-insert children list.
/// The parsed subtree stays in the document's <c>_byId</c> store (orphan-style) so that
/// <see cref="Apply"/> on redo can re-splice without re-parsing. This matches the same
/// orphan pattern used by <see cref="DeleteNodeCommand"/>.
/// </para>
/// <para>
/// <b>ReferencedNodeIds:</b> Exposes all NodeIds allocated for the subtree so that
/// Compact-on-Save preserves them while this command is on the undo or redo stack.
/// </para>
/// <para>
/// <b>Malformed XML:</b> Throws <see cref="System.Xml.XmlException"/> on the first
/// <see cref="Apply"/> if the XML string is not well-formed. The document state is
/// unchanged when the exception propagates (parsing is attempted before any mutation).
/// </para>
/// </remarks>
public sealed class InsertSubtreeCommand : PlatformEditorCommand
{
    private readonly XmlDocument _document;
    private readonly NodeId _parentId;
    private readonly string _xml;

    // Allocated on first Apply; reused on subsequent Apply (redo) calls.
    private NodeId _insertedRootId;

    // All NodeIds allocated for the subtree — kept so ReferencedNodeIds can return them.
    private readonly List<NodeId> _subtreeIds = new();

    // Snapshot of the parent before Apply — used by Revert to restore original children.
    private XdmElement? _previousParent;

    /// <summary>
    /// Initializes a new <see cref="InsertSubtreeCommand"/>.
    /// </summary>
    /// <param name="document">The document whose node store will be mutated.</param>
    /// <param name="parentId">The <see cref="NodeId"/> of the parent element to insert into.</param>
    /// <param name="xml">Well-formed XML text. The document element becomes the inserted subtree root.</param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="xml"/> is <see langword="null"/>.</exception>
    public InsertSubtreeCommand(XmlDocument document, NodeId parentId, string xml)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(xml);
        _document = document;
        _parentId = parentId;
        _xml = xml;
    }

    /// <inheritdoc/>
    public string Description => "Paste subtree";

    /// <inheritdoc/>
    /// <remarks>
    /// Returns all NodeIds allocated for the inserted subtree so Compact-on-Save keeps them
    /// alive while this command is on either the undo or redo stack.
    /// </remarks>
    public IEnumerable<NodeId> ReferencedNodeIds => _subtreeIds;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The target parent node is not an <see cref="XdmElement"/>.</exception>
    /// <exception cref="System.Xml.XmlException">The XML string is malformed (first Apply only).</exception>
    public void Apply(PlatformDocument document)
    {
        var parent = _document.Resolve(_parentId) as XdmElement
            ?? throw new InvalidOperationException(
                $"Node {_parentId} is not an XdmElement; cannot insert subtree child.");

        _previousParent = parent;

        if (_insertedRootId == default)
        {
            // First Apply: parse the XML and adopt it into this document.
            // ParseSubtreeXml allocates NodeIds via AllocateNodeId and registers each node
            // via AddNode, then returns the subtree root. It throws XmlException on malformed input
            // before any mutation occurs beyond namespace table merges (which is benign).
            var subtreeRoot = _document.ParseSubtreeXml(_xml);
            _insertedRootId = subtreeRoot.Id;

            // Collect all NodeIds in the subtree for ReferencedNodeIds.
            _subtreeIds.Clear();
            CollectSubtree(subtreeRoot, _subtreeIds);
        }
        // Subsequent Apply (redo): _insertedRootId is already set and the subtree is still
        // in _byId (Revert leaves orphans; Compact-on-Save won't evict while we're referenced).

        var newChildren = new List<NodeId>(parent.Children) { _insertedRootId };
        var updatedParent = WithChildren(parent, newChildren.ToImmutableArray());
        _document.ReplaceNode(_parentId, updatedParent);
    }

    /// <inheritdoc/>
    public void Revert(PlatformDocument document)
    {
        if (_previousParent is null) return;
        // Restore the parent without the inserted subtree root.
        // The subtree stays in _byId; Compact-on-Save evicts it when this command
        // leaves both the undo and redo stacks.
        _document.ReplaceNode(_parentId, _previousParent);
    }

    /// <inheritdoc/>
    public bool TryMergeWith(PlatformEditorCommand previous, out PlatformEditorCommand merged)
    {
        // Subtree insertions never merge.
        merged = this;
        return false;
    }

    /// <summary>
    /// Recursively collects all <see cref="NodeId"/> values in the subtree rooted at
    /// <paramref name="node"/> (including attribute nodes).
    /// </summary>
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
}
