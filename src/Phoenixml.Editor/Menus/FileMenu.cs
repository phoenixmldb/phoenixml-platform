using Avalonia.Controls;
using Avalonia.Layout;
using Phoenixml.Editor.Commands;

namespace Phoenixml.Editor.Menus;

internal static class FileMenu
{
    public static Menu Build(EditorMainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var openItem = new MenuItem { Header = "_Open..." };
        openItem.Click += async (_, _) =>
            await OpenFileCommand.ExecuteAsync(window, CancellationToken.None);

        var saveItem = new MenuItem { Header = "_Save" };
        saveItem.Click += async (_, _) =>
            await SaveFileCommand.ExecuteAsync(window, CancellationToken.None);

        var saveAsItem = new MenuItem { Header = "Save _As..." };
        saveAsItem.Click += async (_, _) =>
            await SaveAsFileCommand.ExecuteAsync(window, CancellationToken.None);

        var exitItem = new MenuItem { Header = "E_xit" };
        exitItem.Click += (_, _) => ExitCommand.Execute(window.Window);

        var fileMenu = new MenuItem
        {
            Header = "_File",
            ItemsSource = new object[] { openItem, saveItem, saveAsItem, new Separator(), exitItem },
        };

        return new Menu { ItemsSource = new[] { fileMenu } };
    }
}
