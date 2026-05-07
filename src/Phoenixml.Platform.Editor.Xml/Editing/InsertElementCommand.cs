using System.Collections.Immutable;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Abstractions.Documents;
using PlatformDocument = Phoenixml.Platform.Abstractions.Documents.IDocument;
using PlatformEditorCommand = Phoenixml.Platform.Abstractions.Documents.IEditorCommand;

namespace Phoenixml.Platform.Editor.Xml.Editing;

/// <summary>
/// Inserts a new empty <c>&lt;localName/&gt;</c> element as a child of the specified parent element.
/// The element is appended by default; pass an insert index to insert at a specific position.
/// Revert restores the parent's original children list; the inserted node stays in
/// <c>_byId</c> until Compact-on-Save sweeps it.
/// </summary>
public sealed class InsertElementCommand : PlatformEditorCommand
{
    private readonly XmlDocument _document;
    private readonly NodeId _parentId;
    private readonly string _localName;
    private readonly int? _insertIndex;

    // Allocated on first Apply; reused on subsequent Apply (redo) calls.
    private NodeId _insertedId;
    private int _resolvedIndex;
    private XdmElement? _previousParent;

    /// <summary>
    /// Initializes a new <see cref="InsertElementCommand"/>.
    /// </summary>
    /// <param name="document">The document whose node store will be mutated.</param>
    /// <param name="parentId">The <see cref="NodeId"/> of the parent element to insert into.</param>
    /// <param name="localName">The local name for the new element.</param>
    /// <param name="insertIndex">
    /// The zero-based index at which to insert the child, or <c>null</c> to append.
    /// </param>
    public InsertElementCommand(
        XmlDocument document,
        NodeId parentId,
        string localName,
        int? insertIndex = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(localName);
        _document = document;
        _parentId = parentId;
        _localName = localName;
        _insertIndex = insertIndex;
    }

    /// <inheritdoc/>
    public string Description => $"Insert <{_localName}>";

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the inserted NodeId once allocated so that Compact-on-Save preserves the orphaned
    /// node across Revert + redo state (i.e., while this command is on the redo stack).
    /// </remarks>
    public IEnumerable<NodeId> ReferencedNodeIds =>
        _insertedId == default
            ? Array.Empty<NodeId>()
            : new[] { _insertedId };

    /// <inheritdoc/>
    public void Apply(PlatformDocument document)
    {
        var parent = _document.Resolve(_parentId) as XdmElement
            ?? throw new InvalidOperationException(
                $"Node {_parentId} is not an XdmElement; cannot insert child.");
        _previousParent = parent;

        if (_insertedId == default)
        {
            // First Apply: allocate a fresh NodeId and add the node to _byId.
            _insertedId = _document.AllocateNodeId();
            _resolvedIndex = _insertIndex ?? parent.Children.Count;

            var newElement = new XdmElement
            {
                Id = _insertedId,
                Document = parent.Document,
                Namespace = NamespaceId.None,
                Prefix = null,
                LocalName = _localName,
                Parent = _parentId,
                Attributes = XdmElement.EmptyAttributes,
                Children = XdmElement.EmptyChildren,
                NamespaceDeclarations = XdmElement.EmptyNamespaceDeclarations,
            };
            _document.AddNode(newElement);
        }
        // Subsequent Apply (redo): _insertedId is already set; node is still in _byId from
        // the Revert path (which leaves orphans in place). Just splice it back in.

        var newChildren = new List<NodeId>(parent.Children);
        newChildren.Insert(_resolvedIndex, _insertedId);
        var updatedParent = WithChildren(parent, newChildren);
        _document.ReplaceNode(_parentId, updatedParent);
    }

    /// <inheritdoc/>
    public void Revert(PlatformDocument document)
    {
        if (_previousParent is null) return;
        // Restore the parent without the inserted child.
        // The inserted node stays in _byId; Compact-on-Save evicts it
        // when this command leaves both the undo and redo stacks.
        _document.ReplaceNode(_parentId, _previousParent);
    }

    /// <inheritdoc/>
    public bool TryMergeWith(PlatformEditorCommand previous, out PlatformEditorCommand merged)
    {
        // Insertions never merge.
        merged = this;
        return false;
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
