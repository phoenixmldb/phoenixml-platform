using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Phoenixml.Editor.Quit;

/// <summary>
/// Modal dialog shown when the editor window closes with one or more documents that have
/// unsaved changes. Presents three choices: Save All, Discard All, or Cancel (keep the
/// window open).
/// </summary>
internal sealed class DirtyDocsConfirmDialog : Window
{
    /// <summary>The action the user chose.</summary>
    public enum Result
    {
        /// <summary>Save all dirty documents before closing.</summary>
        SaveAll,

        /// <summary>Discard all unsaved changes and close without saving.</summary>
        DiscardAll,

        /// <summary>Cancel the close and return to the editor.</summary>
        Cancel,
    }

    /// <summary>Gets the outcome after the dialog has been dismissed.</summary>
    public Result Outcome { get; private set; } = Result.Cancel;

    /// <summary>
    /// Initializes a new <see cref="DirtyDocsConfirmDialog"/> listing the supplied
    /// <paramref name="displayNames"/>.
    /// </summary>
    /// <param name="displayNames">Display names of every document with unsaved changes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="displayNames"/> is <see langword="null"/>.</exception>
    public DirtyDocsConfirmDialog(IReadOnlyList<string> displayNames)
    {
        ArgumentNullException.ThrowIfNull(displayNames);

        Title = "Unsaved changes";
        Width = 420;
        Height = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var list = new ListBox { ItemsSource = displayNames, Margin = new Thickness(8) };

        var saveAll = new Button { Content = "Save All", Margin = new Thickness(4) };
        saveAll.Click += (_, _) => { Outcome = Result.SaveAll; Close(); };

        var discardAll = new Button { Content = "Discard All", Margin = new Thickness(4) };
        discardAll.Click += (_, _) => { Outcome = Result.DiscardAll; Close(); };

        var cancel = new Button { Content = "Cancel", Margin = new Thickness(4) };
        cancel.Click += (_, _) => { Outcome = Result.Cancel; Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8),
            Children = { saveAll, discardAll, cancel },
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var header = new TextBlock
        {
            Text = "The following documents have unsaved changes:",
            Margin = new Thickness(8),
            TextWrapping = TextWrapping.Wrap,
        };
        DockPanel.SetDock(header, Dock.Top);

        var root = new DockPanel { LastChildFill = true };
        root.Children.Add(header);
        root.Children.Add(buttons);
        root.Children.Add(list);
        Content = root;
    }
}
