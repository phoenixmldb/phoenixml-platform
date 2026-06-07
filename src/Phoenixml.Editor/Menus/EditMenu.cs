using Avalonia.Controls;
using Avalonia.Input;
using Phoenixml.Workbench.Commands;

namespace Phoenixml.Editor.Menus;

/// <summary>
/// Builds the <c>Edit</c> top-level menu item containing Undo and Redo entries.
/// </summary>
internal static class EditMenu
{
    /// <summary>
    /// Creates and returns the <c>_Edit</c> <see cref="MenuItem"/> with Undo and Redo child items.
    /// </summary>
    /// <param name="window">The editor window whose active document is targeted by undo/redo.</param>
    /// <returns>A <see cref="MenuItem"/> ready to be added to the top-level <see cref="Menu"/>.</returns>
    public static MenuItem Build(EditorMainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var undoItem = new MenuItem
        {
            Header = "_Undo",
            InputGesture = new KeyGesture(Key.Z, KeyModifiers.Control),
        };
        undoItem.Click += (_, _) => UndoCommand.Execute(window.State);

        var redoItem = new MenuItem
        {
            Header = "_Redo",
            InputGesture = new KeyGesture(Key.Y, KeyModifiers.Control),
        };
        redoItem.Click += (_, _) => RedoCommand.Execute(window.State);

        return new MenuItem
        {
            Header = "_Edit",
            ItemsSource = new object[] { undoItem, redoItem },
        };
    }
}
