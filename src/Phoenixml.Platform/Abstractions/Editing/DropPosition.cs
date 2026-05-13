namespace Phoenixml.Platform.Abstractions.Editing;

/// <summary>
/// Where a dragged tree row should be placed relative to its drop-target row.
/// </summary>
/// <remarks>
/// <para>
/// Computed from the cursor's vertical position within the target row at drop time
/// (top third, middle, or bottom third). Three positions cover the XML Notepad idiom:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="AsLastChild"/> — drop in the middle of the row; insert as last child of the target.</description></item>
/// <item><description><see cref="BeforeTarget"/> — drop in the top third; insert as the immediate preceding sibling of the target.</description></item>
/// <item><description><see cref="AfterTarget"/> — drop in the bottom third; insert as the immediate following sibling of the target.</description></item>
/// </list>
/// <para>
/// There is intentionally no "as first child" — drop above the first child of the target
/// element produces the same effect.
/// </para>
/// </remarks>
public enum DropPosition
{
    /// <summary>Insert the dragged subtree as the last child of the drop-target.</summary>
    AsLastChild,

    /// <summary>Insert the dragged subtree as the preceding sibling of the drop-target.</summary>
    BeforeTarget,

    /// <summary>Insert the dragged subtree as the following sibling of the drop-target.</summary>
    AfterTarget,
}
