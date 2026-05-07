using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Abstractions.Documents;
using PlatformDocument = Phoenixml.Platform.Abstractions.Documents.IDocument;
using PlatformEditorCommand = Phoenixml.Platform.Abstractions.Documents.IEditorCommand;

namespace Phoenixml.Platform.Editor.Xml.Editing;

/// <summary>
/// Renames an XML attribute by replacing its <see cref="XdmAttribute.LocalName"/>.
/// Preserves the attribute's value, namespace, prefix, type annotation, identity flags,
/// and node identity (<see cref="XdmNode.Id"/>).
/// </summary>
public sealed class RenameAttributeCommand : PlatformEditorCommand
{
    private readonly XmlDocument _document;
    private readonly NodeId _attributeId;
    private readonly string _newName;
    private XdmAttribute? _previous;

    /// <summary>
    /// Initializes a new <see cref="RenameAttributeCommand"/>.
    /// </summary>
    /// <param name="document">The document whose node store will be mutated.</param>
    /// <param name="attributeId">The NodeId of the attribute to rename.</param>
    /// <param name="newName">The replacement local name.</param>
    public RenameAttributeCommand(XmlDocument document, NodeId attributeId, string newName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(newName);
        _document = document;
        _attributeId = attributeId;
        _newName = newName;
    }

    /// <inheritdoc/>
    public string Description => $"Rename attribute to '@{_newName}'";

    /// <inheritdoc/>
    public IEnumerable<NodeId> ReferencedNodeIds => Array.Empty<NodeId>();

    /// <inheritdoc/>
    public void Apply(PlatformDocument document)
    {
        var current = _document.Resolve(_attributeId) as XdmAttribute
            ?? throw new InvalidOperationException(
                $"Node {_attributeId} is not an XdmAttribute; cannot rename.");
        _previous = current;
        var renamed = WithLocalName(current, _newName);
        _document.ReplaceNode(_attributeId, renamed);
    }

    /// <inheritdoc/>
    public void Revert(PlatformDocument document)
    {
        if (_previous is null) return;
        _document.ReplaceNode(_attributeId, _previous);
    }

    /// <inheritdoc/>
    public bool TryMergeWith(PlatformEditorCommand previous, out PlatformEditorCommand merged)
    {
        // Two consecutive renames of the same attribute collapse into one undo step:
        // user types char-by-char; a single Undo should restore the original name.
        //
        // UndoStack calls: stackTop.TryMergeWith(newCommand, out merged)
        //   this     = the already-applied command on the stack (has _previous = original snapshot)
        //   previous = the new command being executed (not yet applied; _previous is null)
        if (previous is RenameAttributeCommand next
            && next._document == _document
            && next._attributeId == _attributeId)
        {
            merged = new RenameAttributeCommand(_document, _attributeId, next._newName)
            {
                _previous = _previous,  // preserve the ORIGINAL pre-rename attribute (from this)
            };
            return true;
        }

        merged = this;
        return false;
    }

    private static XdmAttribute WithLocalName(XdmAttribute source, string newName) =>
        new()
        {
            Id = source.Id,
            Document = source.Document,
            Namespace = source.Namespace,
            Prefix = source.Prefix,
            LocalName = newName,
            Parent = source.Parent,
            Value = source.Value,
            TypeAnnotation = source.TypeAnnotation,
            IsId = source.IsId,
            IsIdRef = source.IsIdRef,
        };
}
