using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Phoenixml.Platform.Editor.Xml;

namespace Phoenixml.Editor.Explorer;

/// <summary>
/// VSCode-style left column. Shows a folder tree of XML files (.xml, .xsd, .xsl, .xslt, .sch)
/// rooted at the parent directory of the active document, or at a folder the user picked
/// explicitly via the "Open Folder…" button. Double-clicking a file routes through
/// <see cref="EditorMainWindow.OpenFileOnLaunch"/>.
/// </summary>
internal sealed class EditorExplorer : DockPanel
{
    private readonly EditorMainWindow _window;
    private readonly TreeView _tree;
    private readonly TextBlock _rootLabel;
    private readonly ObservableCollection<ExplorerNode> _roots = [];

    public EditorExplorer(EditorMainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));
        LastChildFill = true;

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4),
        };
        var openFolder = new Button
        {
            Content = "Open Folder…",
            Padding = new Thickness(6, 2),
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
        };
        openFolder.Click += async (_, _) => await PickFolderAsync().ConfigureAwait(false);
        toolbar.Children.Add(openFolder);
        DockPanel.SetDock(toolbar, Dock.Top);
        Children.Add(toolbar);

        _rootLabel = new TextBlock
        {
            Margin = new Thickness(8, 0, 8, 4),
            Foreground = new SolidColorBrush(Color.FromRgb(0xa0, 0xa0, 0xa0)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = "(no folder open)",
        };
        DockPanel.SetDock(_rootLabel, Dock.Top);
        Children.Add(_rootLabel);

        _tree = new TreeView
        {
            ItemsSource = _roots,
            Background = Brushes.Transparent,
        };
        _tree.ItemTemplate = new FuncTreeDataTemplate<ExplorerNode>(
            (n, _) => BuildRow(n),
            n => n.Children);
        _tree.DoubleTapped += OnTreeDoubleTapped;
        Children.Add(_tree);

        // Auto-set root to the active document's folder.
        _window.State.ActiveDocumentChanged += OnActiveDocumentChanged;
        OnActiveDocumentChanged(_window.State.ActiveDocument);

        // Lazy-load children when a folder is expanded.
        _tree.AddHandler(TreeViewItem.ExpandedEvent, OnTreeViewItemExpanded);
    }

    private static StackPanel BuildRow(ExplorerNode node)
    {
        var icon = new TextBlock
        {
            Text = node.IsDirectory ? "▸" : "•",
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
        };
        var label = new TextBlock
        {
            Text = node.DisplayName,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { icon, label },
        };
    }

    private string? _currentRoot;

    private void OnActiveDocumentChanged(XmlDocument? doc)
    {
        if (doc?.Source?.LocalPath is not string path) return;
        var folder = Path.GetDirectoryName(path);
        if (folder is null) return;

        // Don't disturb the user's chosen root if the newly-opened file is already
        // under it — most likely they double-clicked it from the tree itself, or
        // opened a sibling via Recent Files. Re-root only when the file is elsewhere.
        if (_currentRoot is not null && IsUnderRoot(folder, _currentRoot)) return;

        SetRoot(folder);
    }

    internal void SetRoot(string folder)
    {
        _roots.Clear();
        if (!Directory.Exists(folder))
        {
            _rootLabel.Text = "(folder not found)";
            _currentRoot = null;
            return;
        }
        _rootLabel.Text = folder;
        _currentRoot = Path.GetFullPath(folder);
        var rootNode = new ExplorerNode(folder);
        rootNode.EnsureChildrenLoaded();
        _roots.Add(rootNode);
    }

    private static bool IsUnderRoot(string folder, string root)
    {
        var folderFull = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        var rootFull = root.TrimEnd(Path.DirectorySeparatorChar);
        if (folderFull.Equals(rootFull, StringComparison.Ordinal)) return true;
        return folderFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private void OnTreeViewItemExpanded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (e.Source is TreeViewItem tvi && tvi.DataContext is ExplorerNode node)
            node.EnsureChildrenLoaded();
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_tree.SelectedItem is not ExplorerNode node) return;
        // Always handle the event so Avalonia's default double-click toggle doesn't
        // collapse the folder we just expanded. For files, route to Open; for folders,
        // single-click on the chevron is the (correct) expand gesture.
        e.Handled = true;
        if (node.IsDirectory) return;
        _window.OpenFileOnLaunch(node.Path);
    }

    private async System.Threading.Tasks.Task PickFolderAsync()
    {
        var topLevel = TopLevel.GetTopLevel(_window.Window);
        if (topLevel is null) return;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Folder",
            AllowMultiple = false,
        });
        if (folders is null || folders.Count == 0) return;
        if (folders[0].TryGetLocalPath() is string path)
            SetRoot(path);
    }
}
