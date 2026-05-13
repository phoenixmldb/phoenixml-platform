using System.Collections.Generic;
using System.Collections.Immutable;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Abstractions.Documents;
using PlatformDocument = Phoenixml.Platform.Abstractions.Documents.IDocument;
using PlatformEditorCommand = Phoenixml.Platform.Abstractions.Documents.IEditorCommand;

namespace Phoenixml.Platform.Editor.Xml.Editing;

/// <summary>
/// Moves a subtree from its current parent to a new parent (or a new position within the same
/// parent), preserving its <see cref="NodeId"/> identity. Powers drag-and-drop reorder in the
/// tree editor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity-preserving:</b> Unlike serialize-then-paste, the moved subtree's NodeId stays
/// stable so the editor's selection survives the move and external references remain valid.
/// The Children lists of the source's old parent and the new target parent are spliced
/// directly; nothing is reallocated.
/// </para>
/// <para>
/// <b>Cycle prevention:</b> Apply throws <see cref="InvalidOperationException"/> if the target
/// parent is the source itself or any descendant of the source. The check walks the source's
/// subtree on every Apply (cheap relative to the broader move).
/// </para>
/// <para>
/// <b>Same-parent reorder:</b> When the source's old parent equals the target parent, the
/// splice is performed in a single ReplaceNode so the index math is consistent. If the
/// insert index is greater than the old index, the removal shifts subsequent indices down
/// by one and the insert position is adjusted accordingly.
/// </para>
/// </remarks>
public sealed class MoveSubtreeCommand : PlatformEditorCommand
{
    private readonly XmlDocument _document;
    private readonly NodeId _sourceId;
    private readonly NodeId _targetParentId;
    private readonly int _insertIndex;

    // Captured on first Apply for Revert.
    private NodeId _oldParentId;
    private int _oldIndex;
    private XdmElement? _previousOldParent;
    private XdmElement? _previousTargetParent;
    private XdmElement? _previousSource;

    /// <summary>
    /// Initializes a new <see cref="MoveSubtreeCommand"/>.
    /// </summary>
    /// <param name="document">The document whose node store will be mutated.</param>
    /// <param name="sourceId">The <see cref="NodeId"/> of the subtree root to move.</param>
    /// <param name="targetParentId">The <see cref="NodeId"/> of the destination parent element.</param>
    /// <param name="insertIndex">
    /// The 0-based index in the target parent's Children list at which the moved subtree should
    /// be inserted. When the source's old parent equals the target parent and the insert index
    /// is greater than the source's old index, the post-remove shift is applied automatically.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public MoveSubtreeCommand(XmlDocument document, NodeId sourceId, NodeId targetParentId, int insertIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        _sourceId = sourceId;
        _targetParentId = targetParentId;
        _insertIndex = insertIndex;
    }

    /// <inheritdoc/>
    public string Description => "Move subtree";

    /// <inheritdoc/>
    /// <remarks>
    /// Empty — the moved subtree stays reachable from the target parent's Children list, so
    /// Compact-on-Save will retain it through normal reachability without explicit references.
    /// </remarks>
    public IEnumerable<NodeId> ReferencedNodeIds => Array.Empty<NodeId>();

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// The source or target parent cannot be resolved as an <see cref="XdmElement"/>, the
    /// source has no parent (e.g. document root), the source is not actually a child of its
    /// declared parent, or the target parent is the source itself or a descendant of the
    /// source (cycle).
    /// </exception>
    public void Apply(PlatformDocument document)
    {
        var source = _document.Resolve(_sourceId) as XdmElement
            ?? throw new InvalidOperationException(
                $"Source {_sourceId} is not an XdmElement; cannot move.");

        // Cycle prevention: target parent must not be the source itself nor any descendant.
        if (_targetParentId == _sourceId || IsDescendant(source, _targetParentId))
            throw new InvalidOperationException(
                $"Cannot move {_sourceId} into {_targetParentId}: would create a cycle.");

        var oldParentId = source.Parent
            ?? throw new InvalidOperationException(
                $"Source {_sourceId} has no parent; cannot move (is it the document root?).");
        _oldParentId = oldParentId;

        var oldParent = _document.Resolve(oldParentId) as XdmElement
            ?? throw new InvalidOperationException(
                $"Old parent {oldParentId} is not an XdmElement.");

        var targetParent = _document.Resolve(_targetParentId) as XdmElement
            ?? throw new InvalidOperationException(
                $"Target parent {_targetParentId} is not an XdmElement.");

        var oldIndex = IndexOf(oldParent.Children, _sourceId);
        if (oldIndex < 0)
            throw new InvalidOperationException(
                $"Source {_sourceId} is not a child of its declared parent {oldParentId}.");
        _oldIndex = oldIndex;

        _previousOldParent = oldParent;
        _previousTargetParent = targetParent;
        _previousSource = source;

        // Step 1: update source's Parent pointer to the target parent.
        if (oldParentId != _targetParentId)
            _document.ReplaceNode(_sourceId, WithParent(source, _targetParentId));

        if (oldParentId == _targetParentId)
        {
            // Same-parent reorder: splice in a single ReplaceNode so the index math is
            // consistent. If insertIndex > oldIndex, the post-remove shift means the
            // effective insert position is insertIndex - 1.
            var adjustedInsert = _insertIndex > oldIndex ? _insertIndex - 1 : _insertIndex;
            if (adjustedInsert < 0) adjustedInsert = 0;
            if (adjustedInsert > oldParent.Children.Count - 1) adjustedInsert = oldParent.Children.Count - 1;

            var rebuilt = ImmutableArray.CreateBuilder<NodeId>(oldParent.Children.Count);
            for (int i = 0; i < oldParent.Children.Count; i++)
            {
                if (i == oldIndex) continue;
                rebuilt.Add(oldParent.Children[i]);
            }
            rebuilt.Insert(adjustedInsert, _sourceId);
            _document.ReplaceNode(oldParentId, WithChildren(oldParent, rebuilt.ToImmutable()));
        }
        else
        {
            // Different parents: remove from old parent, insert into target parent.
            var oldRebuilt = ImmutableArray.CreateBuilder<NodeId>(oldParent.Children.Count - 1);
            for (int i = 0; i < oldParent.Children.Count; i++)
            {
                if (i == oldIndex) continue;
                oldRebuilt.Add(oldParent.Children[i]);
            }
            _document.ReplaceNode(oldParentId, WithChildren(oldParent, oldRebuilt.ToImmutable()));

            var clampedInsert = _insertIndex;
            if (clampedInsert < 0) clampedInsert = 0;
            if (clampedInsert > targetParent.Children.Count) clampedInsert = targetParent.Children.Count;

            var targetRebuilt = ImmutableArray.CreateBuilder<NodeId>(targetParent.Children.Count + 1);
            for (int i = 0; i < targetParent.Children.Count; i++)
            {
                if (i == clampedInsert) targetRebuilt.Add(_sourceId);
                targetRebuilt.Add(targetParent.Children[i]);
            }
            if (clampedInsert == targetParent.Children.Count) targetRebuilt.Add(_sourceId);
            _document.ReplaceNode(_targetParentId, WithChildren(targetParent, targetRebuilt.ToImmutable()));
        }
    }

    /// <inheritdoc/>
    public void Revert(PlatformDocument document)
    {
        if (_previousOldParent is null || _previousTargetParent is null || _previousSource is null) return;

        // Restore source's Parent pointer to the old parent.
        _document.ReplaceNode(_sourceId, _previousSource);

        // Restore both parents to their pre-Apply state. Order doesn't matter — ReplaceNode
        // just swaps entries in _byId.
        _document.ReplaceNode(_oldParentId, _previousOldParent);
        if (_oldParentId != _targetParentId)
            _document.ReplaceNode(_targetParentId, _previousTargetParent);
    }

    /// <inheritdoc/>
    public bool TryMergeWith(PlatformEditorCommand previous, out PlatformEditorCommand merged)
    {
        // Each drag-drop is a discrete user gesture; never merge.
        merged = this;
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="candidateId"/> appears anywhere in
    /// the subtree rooted at <paramref name="root"/> (excluding the root itself).
    /// </summary>
    private bool IsDescendant(XdmElement root, NodeId candidateId)
    {
        foreach (var childId in root.Children)
        {
            if (childId == candidateId) return true;
            if (_document.Resolve(childId) is XdmElement childEl && IsDescendant(childEl, candidateId))
                return true;
        }
        return false;
    }

    private static int IndexOf(IReadOnlyList<NodeId> list, NodeId target)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == target) return i;
        return -1;
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

    private static XdmElement WithParent(XdmElement source, NodeId newParent) =>
        new()
        {
            Id = source.Id,
            Document = source.Document,
            Namespace = source.Namespace,
            Prefix = source.Prefix,
            LocalName = source.LocalName,
            Parent = newParent,
            Attributes = source.Attributes,
            Children = source.Children,
            NamespaceDeclarations = source.NamespaceDeclarations,
            TypeAnnotation = source.TypeAnnotation,
            IsIdContent = source.IsIdContent,
        };
}
