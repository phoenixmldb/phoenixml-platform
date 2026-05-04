namespace Phoenixml.Platform.Abstractions.Editing;

/// <summary>
/// Provides nodes for <c>TreeEditorView</c> to render. Vocabulary packages
/// (e.g. Phoenixml.Platform.Editor.Xml) supply concrete implementations that adapt
/// their domain model (XDM trees, XSLT stylesheets, etc.) to a generic tree.
/// </summary>
public interface ITreeNodeProvider
{
    /// <summary>Root nodes of the tree.</summary>
    IReadOnlyList<ITreeNode> Roots { get; }

    /// <summary>Enumerates the children of <paramref name="node"/>; called lazily by the tree.</summary>
    IReadOnlyList<ITreeNode> GetChildren(ITreeNode node);

    /// <summary>Raised when the underlying tree changes structurally and the view should refresh.</summary>
#pragma warning disable CA1003 // Use generic event handler instances — intentional; matches Action<T> pattern in Abstractions
    event Action? Changed;
#pragma warning restore CA1003
}
