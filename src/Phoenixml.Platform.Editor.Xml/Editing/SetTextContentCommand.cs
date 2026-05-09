using System.Collections.Immutable;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Abstractions.Documents;
using PlatformDocument = Phoenixml.Platform.Abstractions.Documents.IDocument;
using PlatformEditorCommand = Phoenixml.Platform.Abstractions.Documents.IEditorCommand;

namespace Phoenixml.Platform.Editor.Xml.Editing;

/// <summary>
/// Sets the text content of an element. Supports two scenarios:
/// <list type="bullet">
///   <item>
///     <description>
///       <b>ReplacedExistingText</b> — the element already has a single <see cref="XdmText"/> child.
///       Only the text node's <see cref="XdmText.Value"/> is swapped; the element's <c>Children</c>
///       list is unchanged.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>CreatedNewText</b> — the element has no children at all (empty element). A fresh
///       <see cref="XdmText"/> node is allocated, added to the document store, and the element is
///       replaced with a new version whose <c>Children</c> list contains the new text node's id.
///     </description>
///   </item>
/// </list>
/// </summary>
/// <remarks>
/// Mixed-content elements (those with element children) are rejected with an
/// <see cref="InvalidOperationException"/> — editing the value column on those is the wrong UX.
/// </remarks>
public sealed class SetTextContentCommand : PlatformEditorCommand
{
    private enum PathTaken { None, ReplacedExistingText, CreatedNewText }

    private readonly XmlDocument _document;
    private readonly NodeId _elementId;
    private readonly string _newText;

    // ReplacedExistingText path fields (carry over from v0.3).
    private XdmText? _previousTextNode;
    private NodeId _textNodeId;

    // CreatedNewText path fields.
    private PathTaken _path = PathTaken.None;
    private XdmElement? _previousElement; // snapshot of element before the first Apply
    private NodeId _newTextId;            // allocated once; reused on redo

    /// <summary>
    /// Initializes a new <see cref="SetTextContentCommand"/>.
    /// </summary>
    /// <param name="document">The document whose node store will be mutated.</param>
    /// <param name="elementId">The <see cref="NodeId"/> of the element to update.</param>
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
    public IEnumerable<NodeId> ReferencedNodeIds =>
        _path == PathTaken.CreatedNewText && _newTextId != default
            ? new[] { _newTextId }
            : Array.Empty<NodeId>();

    /// <inheritdoc/>
    public void Apply(PlatformDocument document)
    {
        var element = _document.Resolve(_elementId) as XdmElement
            ?? throw new InvalidOperationException(
                $"Node {_elementId} is not an XdmElement; cannot set text content.");

        // Classify the element's current content.
        XdmText? textChild = null;
        bool hasElementChildren = false;
        foreach (var childId in element.Children)
        {
            var child = _document.Resolve(childId);
            if (child is XdmElement)
            {
                hasElementChildren = true;
                break;
            }
            if (child is XdmText t)
            {
                if (textChild is not null)
                    throw new InvalidOperationException(
                        $"Element {_elementId} has multiple text children; SetTextContentCommand requires exactly one.");
                textChild = t;
            }
        }

        if (hasElementChildren)
            throw new InvalidOperationException(
                $"Element {_elementId} has element children; SetTextContentCommand only supports text-only or empty elements.");

        if (textChild is null)
        {
            // ── CreatedNewText path ──────────────────────────────────────────────
            // Zero children. Allocate a fresh text node id on the FIRST Apply only;
            // subsequent redo calls reuse the same id (the node is still in _byId).
            _previousElement ??= element;

            if (_path != PathTaken.CreatedNewText)
            {
                // First Apply: allocate a new id and insert the text node.
                _newTextId = _document.AllocateNodeId();
                var newText = new XdmText
                {
                    Id = _newTextId,
                    Document = element.Document,
                    Parent = _elementId,
                    Value = _newText,
                };
                _document.AddNode(newText);
            }
            else
            {
                // Redo: the text node is already in _byId (Revert left it orphaned).
                // Update its value in place by replacing with a fresh instance.
                var existing = (XdmText)_document.Resolve(_newTextId)!;
                var updated = new XdmText
                {
                    Id = _newTextId,
                    Document = existing.Document,
                    Parent = existing.Parent,
                    Value = _newText,
                };
                _document.ReplaceNode(_newTextId, updated);
            }

            // Replace the element with a version that has the text node as its sole child.
            var updatedElement = WithChildren(element, ImmutableArray.Create(_newTextId));
            _document.ReplaceNode(_elementId, updatedElement);
            _path = PathTaken.CreatedNewText;
        }
        else
        {
            // ── ReplacedExistingText path ────────────────────────────────────────
            _previousTextNode = textChild;
            _textNodeId = textChild.Id;
            var updated = WithValue(textChild, _newText);
            _document.ReplaceNode(_textNodeId, updated);
            _path = PathTaken.ReplacedExistingText;
        }
    }

    /// <inheritdoc/>
    public void Revert(PlatformDocument document)
    {
        if (_path == PathTaken.CreatedNewText)
        {
            // Restore the original empty element. The text node stays orphaned in _byId.
            if (_previousElement is not null)
                _document.ReplaceNode(_elementId, _previousElement);
        }
        else if (_path == PathTaken.ReplacedExistingText)
        {
            if (_previousTextNode is null) return;
            _document.ReplaceNode(_textNodeId, _previousTextNode);
        }
    }

    /// <inheritdoc/>
    public bool TryMergeWith(PlatformEditorCommand previous, out PlatformEditorCommand merged)
    {
        // Convention (matches UndoStack call site):
        //   this     = the already-applied command sitting on top of the stack
        //   previous = the NEW command about to be applied (confusingly named, but matches interface)
        //
        // Only merge within the ReplacedExistingText path. The CreatedNewText case
        // (first-time typing into an empty element) gets separate undo entries —
        // predicting the new command's path before Apply runs is not feasible, so we
        // skip the merge to avoid complexity. Acceptable trade-off since this scenario
        // is rare.
        if (previous is SetTextContentCommand next
            && next._document == _document
            && next._elementId == _elementId
            && _path == PathTaken.ReplacedExistingText   // this (stack top) has already run Apply
            && next._path == PathTaken.None)             // new command has not run Apply yet
        {
            merged = new SetTextContentCommand(_document, _elementId, next._newText)
            {
                _previousTextNode = _previousTextNode,  // preserve the ORIGINAL pre-edit text node (from this)
                _textNodeId = _textNodeId,
                _path = PathTaken.ReplacedExistingText,
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

    private static XdmElement WithChildren(XdmElement source, IReadOnlyList<NodeId> newChildren) =>
        new()
        {
            Id = source.Id,
            Document = source.Document,
            Parent = source.Parent,
            Namespace = source.Namespace,
            LocalName = source.LocalName,
            Prefix = source.Prefix,
            Attributes = source.Attributes,
            Children = newChildren,
            NamespaceDeclarations = source.NamespaceDeclarations,
            TypeAnnotation = source.TypeAnnotation,
            IsIdContent = source.IsIdContent,
        };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
