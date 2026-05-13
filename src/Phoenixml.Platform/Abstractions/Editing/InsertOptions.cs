namespace Phoenixml.Platform.Abstractions.Editing;

/// <summary>
/// Populated by the host when a right-click <see cref="Avalonia.Controls.ContextMenu"/> is
/// about to open on an element row. When <see cref="ChildElementNames"/> or
/// <see cref="AttributeNames"/> is non-empty, <c>NodeListView</c> builds a dynamic submenu
/// of those choices for the corresponding Insert menu item. When empty, the existing
/// single-click "Insert Child Element" / "Insert Attribute" gestures remain, firing the
/// original <c>NodeInsertChildElementRequested</c> / <c>NodeInsertAttributeRequested</c> events.
/// </summary>
public sealed class InsertOptions
{
    /// <summary>
    /// Ordered list of valid child element local names derived from the active schema's
    /// content model for the right-clicked element. Empty when no schema is bound or the
    /// parent has no declared children (e.g., simple-typed elements).
    /// </summary>
    public IReadOnlyList<string> ChildElementNames { get; set; } = [];

    /// <summary>
    /// Ordered list of attribute local names declared on the right-clicked element in the
    /// active schema, filtered to exclude attributes that are already present on the element.
    /// Empty when no schema is bound or all declared attributes are already present.
    /// </summary>
    public IReadOnlyList<string> AttributeNames { get; set; } = [];
}
