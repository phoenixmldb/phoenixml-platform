using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Abstractions.Documents;
using PlatformDocument = Phoenixml.Platform.Abstractions.Documents.IDocument;
using PlatformEditorCommand = Phoenixml.Platform.Abstractions.Documents.IEditorCommand;

namespace Phoenixml.Platform.Editor.Xml.Editing;

/// <summary>
/// Replaces the string value of a text-only element's single <see cref="XdmText"/> child.
/// The element itself is unchanged because its <c>Children</c> list still references the
/// same <see cref="NodeId"/> — only the text node's <see cref="XdmText.Value"/> is swapped.
/// </summary>
/// <remarks>
/// This command intentionally only supports text-only elements (elements whose only children
/// are text, comment, or processing-instruction nodes and which have exactly one text child).
/// <para>
/// Empty elements (no text child at all) require allocating a new NodeId and mutating the
/// parent's Children list — this is deferred to Plan 10.
/// </para>
/// <para>
/// Mixed-content elements (those with element children) have no single text value to edit
/// and are also rejected.
/// </para>
/// </remarks>
public sealed class SetTextContentCommand : PlatformEditorCommand
{
    private readonly XmlDocument _document;
    private readonly NodeId _elementId;
    private readonly string _newText;
    private XdmText? _previousTextNode;
    private NodeId _textNodeId;

    /// <summary>
    /// Initializes a new <see cref="SetTextContentCommand"/>.
    /// </summary>
    /// <param name="document">The document whose node store will be mutated.</param>
    /// <param name="elementId">The NodeId of the text-only element to update.</param>
    /// <param name="newText">The replacement text content.</param>
    public SetTextContentCommand(XmlDocument document, NodeId elementId, string newText)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(newText);
        _document = document;
        _elementId = elementId;
        _newText = newText;
    }

    /// <inheritdoc/>
    public string Description => $"Set text to '{Truncate(_newText, 30)}'";

    /// <inheritdoc/>
    public IEnumerable<NodeId> ReferencedNodeIds => Array.Empty<NodeId>();

    /// <inheritdoc/>
    public void Apply(PlatformDocument document)
    {
        var element = _document.Resolve(_elementId) as XdmElement
            ?? throw new InvalidOperationException(
                $"Node {_elementId} is not an XdmElement; cannot set text content.");

        // Locate the single text-child. v0.3 intentionally only supports text-only
        // elements (one XdmText child, no element children). Empty-element and mixed
        // content are deferred to Plan 10.
        XdmText? textChild = null;
        foreach (var childId in element.Children)
        {
            var child = _document.Resolve(childId);
            if (child is XdmElement)
                throw new InvalidOperationException(
                    $"Element {_elementId} has element children; SetTextContentCommand only supports text-only elements.");
            if (child is XdmText t)
            {
                if (textChild is not null)
                    throw new InvalidOperationException(
                        $"Element {_elementId} has multiple text children; SetTextContentCommand requires exactly one.");
                textChild = t;
            }
        }

        if (textChild is null)
            throw new InvalidOperationException(
                $"Element {_elementId} has no text child to update; SetTextContentCommand requires an existing XdmText child (creating one is Plan 10).");

        _previousTextNode = textChild;
        _textNodeId = textChild.Id;

        var updated = WithValue(textChild, _newText);
        _document.ReplaceNode(_textNodeId, updated);
    }

    /// <inheritdoc/>
    public void Revert(PlatformDocument document)
    {
        if (_previousTextNode is null) return;
        _document.ReplaceNode(_textNodeId, _previousTextNode);
    }

    /// <inheritdoc/>
    public bool TryMergeWith(PlatformEditorCommand previous, out PlatformEditorCommand merged)
    {
        // Two consecutive text sets on the same element collapse into one undo step.
        //
        // UndoStack calls: stackTop.TryMergeWith(newCommand, out merged)
        //   this     = the already-applied command on the stack (has _previousTextNode = original)
        //   previous = the new command being executed (not yet applied)
        if (previous is SetTextContentCommand next
            && next._document == _document
            && next._elementId == _elementId)
        {
            merged = new SetTextContentCommand(_document, _elementId, next._newText)
            {
                _previousTextNode = _previousTextNode,  // preserve the ORIGINAL pre-edit text node
                _textNodeId = _textNodeId,
            };
            return true;
        }

        merged = this;
        return false;
    }

    private static XdmText WithValue(XdmText source, string newValue) =>
        new()
        {
            Id = source.Id,
            Document = source.Document,
            Parent = source.Parent,
            Value = newValue,
        };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
