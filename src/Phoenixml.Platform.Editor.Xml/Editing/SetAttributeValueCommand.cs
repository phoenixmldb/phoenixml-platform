using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Abstractions.Documents;
using PlatformDocument = Phoenixml.Platform.Abstractions.Documents.IDocument;
using PlatformEditorCommand = Phoenixml.Platform.Abstractions.Documents.IEditorCommand;

namespace Phoenixml.Platform.Editor.Xml.Editing;

/// <summary>
/// Sets the string value of an XML attribute by replacing its <see cref="XdmAttribute.Value"/>.
/// Preserves the attribute's local name, namespace, prefix, type annotation, identity flags,
/// and node identity (<see cref="XdmNode.Id"/>).
/// </summary>
public sealed class SetAttributeValueCommand : PlatformEditorCommand
{
    private readonly XmlDocument _document;
    private readonly NodeId _attributeId;
    private readonly string _newValue;
    private XdmAttribute? _previous;

    /// <summary>
    /// Initializes a new <see cref="SetAttributeValueCommand"/>.
    /// </summary>
    /// <param name="document">The document whose node store will be mutated.</param>
    /// <param name="attributeId">The NodeId of the attribute to update.</param>
    /// <param name="newValue">The replacement string value.</param>
    public SetAttributeValueCommand(XmlDocument document, NodeId attributeId, string newValue)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(newValue);
        _document = document;
        _attributeId = attributeId;
        _newValue = newValue;
    }

    /// <inheritdoc/>
    public string Description => $"Set attribute value to '{Truncate(_newValue, 30)}'";

    /// <inheritdoc/>
    public IEnumerable<NodeId> ReferencedNodeIds => Array.Empty<NodeId>();

    /// <inheritdoc/>
    public void Apply(PlatformDocument document)
    {
        var current = _document.Resolve(_attributeId) as XdmAttribute
            ?? throw new InvalidOperationException(
                $"Node {_attributeId} is not an XdmAttribute; cannot set value.");
        _previous = current;
        var updated = WithValue(current, _newValue);
        _document.ReplaceNode(_attributeId, updated);
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
        // Two consecutive value sets on the same attribute collapse into one undo step:
        // user types char-by-char; a single Undo should restore the original value.
        //
        // UndoStack calls: stackTop.TryMergeWith(newCommand, out merged)
        //   this     = the already-applied command on the stack (has _previous = original snapshot)
        //   previous = the new command being executed (not yet applied; _previous is null)
        if (previous is SetAttributeValueCommand next
            && next._document == _document
            && next._attributeId == _attributeId)
        {
            merged = new SetAttributeValueCommand(_document, _attributeId, next._newValue)
            {
                _previous = _previous,  // preserve the ORIGINAL pre-edit attribute (from this)
            };
            return true;
        }

        merged = this;
        return false;
    }

    private static XdmAttribute WithValue(XdmAttribute source, string newValue) =>
        new()
        {
            Id = source.Id,
            Document = source.Document,
            Namespace = source.Namespace,
            Prefix = source.Prefix,
            LocalName = source.LocalName,
            Parent = source.Parent,
            Value = newValue,
            TypeAnnotation = source.TypeAnnotation,
            IsId = source.IsId,
            IsIdRef = source.IsIdRef,
        };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
