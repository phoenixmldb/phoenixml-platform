using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons;
using Material.Icons.Avalonia;
using Phoenixml.Editor.Commands;

namespace Phoenixml.Editor.Toolbar;

/// <summary>
/// XmlNotepad-style icon toolbar with the most common file and edit operations.
/// Sits between the menu and the content area.
/// </summary>
internal sealed class EditorToolbar : StackPanel
{
    public EditorToolbar(EditorMainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Orientation = Orientation.Horizontal;
        Margin = new Thickness(4);
        Spacing = 2;

        Children.Add(BuildAsyncButton(MaterialIconKind.FolderOpen, "Open (Ctrl+O)",
            () => OpenFileCommand.ExecuteAsync(window, CancellationToken.None)));
        Children.Add(BuildAsyncButton(MaterialIconKind.ContentSave, "Save (Ctrl+S)",
            () => SaveFileCommand.ExecuteAsync(window, CancellationToken.None)));
        Children.Add(new Border
        {
            Width = 1,
            Margin = new Thickness(4, 2),
            Background = Brushes.Transparent,
        });
        Children.Add(BuildButton(MaterialIconKind.Undo, "Undo (Ctrl+Z)",
            () => UndoCommand.Execute(window)));
        Children.Add(BuildButton(MaterialIconKind.Redo, "Redo (Ctrl+Y)",
            () => RedoCommand.Execute(window)));
    }

    private static Button BuildButton(MaterialIconKind icon, string tooltip, Action onClick)
    {
        var btn = new Button
        {
            Content = new MaterialIcon { Kind = icon, Width = 18, Height = 18 },
            Padding = new Thickness(6),
            Background = Brushes.Transparent,
            BorderThickness = default,
        };
        ToolTip.SetTip(btn, tooltip);
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private static Button BuildAsyncButton(MaterialIconKind icon, string tooltip, Func<Task> onClick)
    {
        var btn = new Button
        {
            Content = new MaterialIcon { Kind = icon, Width = 18, Height = 18 },
            Padding = new Thickness(6),
            Background = Brushes.Transparent,
            BorderThickness = default,
        };
        ToolTip.SetTip(btn, tooltip);
        btn.Click += async (_, _) => await onClick();
        return btn;
    }
}
