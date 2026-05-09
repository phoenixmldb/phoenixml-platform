using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Abstractions.Documents;
using PlatformDocument = Phoenixml.Platform.Abstractions.Documents.IDocument;
using PlatformEditorCommand = Phoenixml.Platform.Abstractions.Documents.IEditorCommand;

namespace Phoenixml.Platform.Editor.Xml.Editing;

/// <summary>
/// Inserts a new attribute with the given local name and value onto the specified parent element.
/// The attribute is always appended to the end of the parent's attribute list. Revert restores the
/// parent's original attribute list; the inserted node stays in <c>_byId</c> until Compact-on-Save
/// sweeps it.
/// </summary>
public sealed class InsertAttributeCommand : PlatformEditorCommand
{
    private readonly XmlDocument _document;
    private readonly NodeId _parentId;
    private readonly string _localName;
    private readonly string _value;

    // Allocated on first Apply; reused on subsequent Apply (redo) calls.
    private NodeId _insertedId;
    private XdmElement? _previousParent;

    /// <summary>
    /// Initializes a new <see cref="InsertAttributeCommand"/>.
    /// </summary>
    /// <param name="document">The document whose node store will be mutated.</param>
    /// <param name="parentId">The <see cref="NodeId"/> of the parent element to insert onto.</param>
    /// <param name="localName">The local name for the new attribute.</param>
    /// <param name="value">The string value for the new attribute.</param>
    public InsertAttributeCommand(
        XmlDocument document,
        NodeId parentId,
        string localName,
        string value)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(localName);
        ArgumentNullException.ThrowIfNull(value);
        _document = document;
        _parentId = parentId;
        _localName = localName;
        _value = value;
    }

    /// <inheritdoc/>
    public string Description => $"Insert @{_localName}";

    /// <summary>
    /// The <see cref="NodeId"/> of the inserted attribute. Available after the first <see cref="Apply"/>;
    /// stable across Revert + redo. Hosts use this to select the new row and enter rename mode.
    /// </summary>
    public NodeId InsertedId => _insertedId;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the inserted NodeId once allocated so that Compact-on-Save preserves the orphaned
    /// attribute across Revert + redo state (i.e., while this command is on the redo stack).
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
                $"Node {_parentId} is not an XdmElement; cannot insert attribute.");
        _previousParent = parent;

        if (_insertedId == default)
        {
            // First Apply: allocate a fresh NodeId and add the attribute to _byId.
            _insertedId = _document.AllocateNodeId();

            var newAttr = new XdmAttribute
            {
                Id = _insertedId,
                Document = parent.Document,
                Namespace = NamespaceId.None,
                Prefix = null,
                LocalName = _localName,
                Parent = _parentId,
                Value = _value,
            };
            _document.AddNode(newAttr);
        }
        // Subsequent Apply (redo): _insertedId is already set; attribute is still in _byId.
        // Just splice it back into the attributes list.

        var newAttrs = new List<NodeId>(parent.Attributes) { _insertedId };
        var updatedParent = WithAttributes(parent, newAttrs);
        _document.ReplaceNode(_parentId, updatedParent);
    }

    /// <inheritdoc/>
    public void Revert(PlatformDocument document)
    {
        if (_previousParent is null) return;
        // Restore the parent without the inserted attribute.
        // The inserted attribute stays in _byId; Compact-on-Save evicts it
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
