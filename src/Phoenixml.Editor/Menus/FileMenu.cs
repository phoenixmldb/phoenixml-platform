using Avalonia.Controls;
using Phoenixml.Workbench.Commands;

namespace Phoenixml.Editor.Menus;

/// <summary>
/// Builds the <c>File</c> top-level menu item containing Open, Save, Save As, and Exit entries.
/// </summary>
internal static class FileMenu
{
    /// <summary>
    /// Creates and returns the <c>_File</c> <see cref="MenuItem"/> with its child items.
    /// </summary>
    /// <param name="window">The editor window whose commands are wired to the menu items.</param>
    /// <returns>A <see cref="MenuItem"/> ready to be added to the top-level <see cref="Menu"/>.</returns>
    public static MenuItem Build(EditorMainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var openItem = new MenuItem { Header = "_Open..." };
        openItem.Click += async (_, _) =>
            await OpenFileCommand.ExecuteAsync(window.FileDialog, window.State, window.Settings, CancellationToken.None);

        var saveItem = new MenuItem { Header = "_Save" };
        saveItem.Click += async (_, _) =>
            await SaveFileCommand.ExecuteAsync(window.FileDialog, window.State, window.Settings, CancellationToken.None);

        var saveAsItem = new MenuItem { Header = "Save _As..." };
        saveAsItem.Click += async (_, _) =>
            await SaveAsFileCommand.ExecuteAsync(window.FileDialog, window.State, window.Settings, CancellationToken.None);

        var setSchemaItem = new MenuItem { Header = "_Set Schema..." };
        setSchemaItem.Click += async (_, _) =>
            await SetSchemaCommand.ExecuteAsync(window, CancellationToken.None);

        var compareItem = new MenuItem { Header = "_Compare With..." };
        compareItem.Click += async (_, _) => await window.CompareWithFileAsync();

        var exitItem = new MenuItem { Header = "E_xit" };
        exitItem.Click += (_, _) => ExitCommand.Execute(window.ShellHost);

        var recentItem = RecentFilesMenu.Build(window, window.Settings);

        return new MenuItem
        {
            Header = "_File",
            ItemsSource = new object[]
            {
                openItem, recentItem, saveItem, saveAsItem, setSchemaItem,
                new Separator(), compareItem, new Separator(), exitItem,
            },
        };
    }
}
