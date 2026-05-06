using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Abstractions.Documents;
using PlatformDocument = Phoenixml.Platform.Abstractions.Documents.IDocument;
using PlatformEditorCommand = Phoenixml.Platform.Abstractions.Documents.IEditorCommand;

namespace Phoenixml.Platform.Editor.Xml.Editing;

/// <summary>
/// Renames an XML element by replacing its LocalName. Preserves attributes,
/// children, namespace, prefix, and identity (NodeId).
/// </summary>
public sealed class RenameElementCommand : PlatformEditorCommand
{
    private readonly XmlDocument _document;
    private readonly NodeId _elementId;
    private readonly string _newName;
    private XdmElement? _previous;

    /// <summary>
    /// Initializes a new <see cref="RenameElementCommand"/>.
    /// </summary>
    /// <param name="document">The document whose node store will be mutated.</param>
    /// <param name="elementId">The NodeId of the element to rename.</param>
    /// <param name="newName">The replacement local name.</param>
    public RenameElementCommand(XmlDocument document, NodeId elementId, string newName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(newName);
        _document = document;
        _elementId = elementId;
        _newName = newName;
    }

    /// <inheritdoc/>
    public string Description => $"Rename element to '{_newName}'";

    /// <inheritdoc/>
    public void Apply(PlatformDocument document)
    {
        var current = _document.Resolve(_elementId) as XdmElement
            ?? throw new InvalidOperationException(
                $"Node {_elementId} is not an XdmElement; cannot rename.");
        _previous = current;
        var renamed = WithLocalName(current, _newName);
        _document.ReplaceNode(_elementId, renamed);
    }

    /// <inheritdoc/>
    public void Revert(PlatformDocument document)
    {
        if (_previous is null) return;
        _document.ReplaceNode(_elementId, _previous);
    }

    /// <inheritdoc/>
    public bool TryMergeWith(PlatformEditorCommand previous, out PlatformEditorCommand merged)
    {
        // Two consecutive renames of the same element collapse into one undo step:
        // user types char-by-char into a textbox; a single Undo should restore the
        // original name, not unwind one keystroke at a time.
        //
        // UndoStack calls: stackTop.TryMergeWith(newCommand, out merged)
        //   this    = the already-applied command on the stack (has _previous = original snapshot)
        //   previous = the new command being executed (not yet applied; _previous is null)
        if (previous is RenameElementCommand next
            && next._document == _document
            && next._elementId == _elementId)
        {
            merged = new RenameElementCommand(_document, _elementId, next._newName)
            {
                _previous = _previous,  // preserve the ORIGINAL pre-rename element (from this)
            };
            return true;
        }

        merged = this;
        return false;
    }

    private static XdmElement WithLocalName(XdmElement source, string newName) =>
        new()
        {
            Id = source.Id,
            Document = source.Document,
            Namespace = source.Namespace,
            Prefix = source.Prefix,
            LocalName = newName,
            Parent = source.Parent,
            Attributes = source.Attributes,
            Children = source.Children,
            NamespaceDeclarations = source.NamespaceDeclarations,
        };
}
