using System.IO;
using Avalonia.Controls;
using Phoenixml.Workbench.Settings;

namespace Phoenixml.Editor.Menus;

/// <summary>
/// Factory that builds the <c>Open _Recent</c> submenu and keeps it synchronised with
/// <see cref="EditorSettings.RecentFiles"/> via the <see cref="EditorSettingsStore.Changed"/> event.
/// </summary>
internal static class RecentFilesMenu
{
    /// <summary>
    /// Creates the <c>Open _Recent</c> <see cref="MenuItem"/> and wires it to rebuild whenever
    /// <see cref="EditorSettingsStore.Changed"/> fires.
    /// </summary>
    /// <param name="window">The editor window used to open files when an entry is clicked.</param>
    /// <param name="store">The settings store whose <c>RecentFiles</c> list drives the submenu.</param>
    /// <returns>A <see cref="MenuItem"/> ready to be inserted into the File menu.</returns>
    public static MenuItem Build(EditorMainWindow window, EditorSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(store);

        var item = new MenuItem { Header = "Open _Recent" };

        void Rebuild()
        {
            var children = new List<object>();
            foreach (var path in store.Settings.RecentFiles)
            {
                var entry = new MenuItem { Header = Path.GetFileName(path) };
                ToolTip.SetTip(entry, path);
                entry.Click += (_, _) =>
                {
                    if (File.Exists(path))
                        window.OpenFileOnLaunch(path);
                };
                children.Add(entry);
            }

            if (children.Count == 0)
                children.Add(new MenuItem { Header = "(no recent files)", IsEnabled = false });

            item.ItemsSource = children;
        }

        store.Changed += Rebuild;
        Rebuild();
        return item;
    }
}
