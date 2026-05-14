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
using Avalonia.VisualTree;
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
#pragma warning disable CA1003
    /// <summary>
    /// Raised when the user picks "Apply Schema" on a right-clicked .xsd row.
    /// The argument is the absolute path to the XSD. Host wires this to
    /// <c>SetPerDocumentSchema</c> + <c>Validation.Revalidate</c>.
    /// </summary>
    public event Action<string>? ApplySchemaRequested;

    /// <summary>
    /// Raised when the user right-clicks an XML file and picks "Apply Schema...".
    /// Argument is the absolute path to the XML file. Host prompts for an XSD and
    /// wires it via <c>SetPerDocumentSchema</c> (Plan 25 #4).
    /// </summary>
    public event Action<string>? ApplySchemaToXmlRequested;
#pragma warning restore CA1003
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
        // Per-row template captures `this` so .xsd rows can build their own ContextMenu
        // instead of sharing a tree-level one (the tree-level + visual-tree-walk approach
        // didn't reliably find the right TreeViewItem on right-click in Avalonia 11.2.4).
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

        // Right-click → "Apply Schema to <active-doc>" on .xsd rows.
        //
        // Diagnostics from a prior session confirmed that ContextRequested fires reliably
        // with the right args.Source, FindAncestorOfType<TreeViewItem> finds the row, and
        // IsXsd correctly identifies xsd files. What DIDN'T work was the canonical
        // `_tree.ContextMenu = menu` + populate-on-ContextRequested pattern — Avalonia 11
        // didn't show items added during the event. So we suppress the default flow with
        // args.Handled = true and Open() a fresh ContextMenu imperatively at the pointer.
        _tree.ContextRequested += (_, args) =>
        {
            args.Handled = true;  // suppress any default attached-menu opening

            if (args.Source is not Visual src) return;
            var tvi = src.FindAncestorOfType<TreeViewItem>(includeSelf: true);
            if (tvi?.DataContext is not ExplorerNode node) return;

            var menu = new ContextMenu
            {
                Placement = PlacementMode.Pointer,
            };

            if (IsXsd(node.Path))
            {
                var doc = _window.State.ActiveDocument;
                var apply = new MenuItem
                {
                    Header = doc is XmlDocument d
                        ? $"Apply Schema to {d.DisplayName}"
                        : "Apply Schema (no document open)",
                    IsEnabled = doc is not null,
                };
                var capturedXsd = node.Path;
                apply.Click += (_, _) => ApplySchemaRequested?.Invoke(capturedXsd);
                menu.Items.Add(apply);
            }
            else if (IsXml(node.Path))
            {
                // Plan 25 #4: pick an XSD from the file system and bind it to this XML doc.
                var apply = new MenuItem { Header = "Apply Schema..." };
                var capturedXml = node.Path;
                apply.Click += (_, _) => ApplySchemaToXmlRequested?.Invoke(capturedXml);
                menu.Items.Add(apply);
            }
            else
            {
                return;
            }

            menu.Open(tvi);
        };
    }

    private static bool IsXsd(string path) =>
        Path.GetExtension(path).Equals(".xsd", StringComparison.OrdinalIgnoreCase);

    private static bool IsXml(string path) =>
        Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase);

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
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            // Transparent background makes the panel hit-test so context-requested
            // gestures land on it even though it's visually invisible. Without this,
            // right-clicks fall through to the parent TreeViewItem.
            Background = Brushes.Transparent,
            Children = { icon, label },
        };
        return panel;
    }

    private string? _currentRoot;

    private void OnActiveDocumentChanged(Phoenixml.Platform.Documents.Document? doc)
    {
        if (doc?.Source?.LocalPath is not string path) return;
        var folder = Path.GetDirectoryName(path);
        if (folder is null) return;

        // Don't disturb the user's chosen root if the newly-opened file is already
        // under it — most likely they double-clicked it from the tree itself, or
        // opened a sibling via Recent Files. Re-root only when the file is elsewhere.
        if (_currentRoot is not null && IsUnderRoot(folder, _currentRoot)) return;

        // Smart auto-root: if the file's parent has a sibling named "schemas" or "xsd"
        // (the data/ + schemas/ layout), root one level UP so both folders are visible
        // — otherwise the user can't see the schemas, can't right-click them, can't take
        // advantage of the explorer at all. The auto-detect heuristic walks outward to
        // find schemas; the explorer should follow the same principle.
        var smartRoot = ChooseSmartRoot(folder);
        SetRoot(smartRoot);
    }

    private static readonly string[] s_schemaSubfolderNames = ["schemas", "xsd"];

    /// <summary>
    /// Chooses where to root the explorer when a document opens. Walks up one level
    /// looking for a sibling <c>schemas/</c> or <c>xsd/</c> folder; if found, roots at
    /// the parent so both the document folder and the schemas folder are visible.
    /// Otherwise roots at the document's own folder.
    /// </summary>
    private static string ChooseSmartRoot(string fileFolder)
    {
        var parent = Path.GetDirectoryName(fileFolder);
        if (parent is null || !Directory.Exists(parent)) return fileFolder;

        foreach (var sub in s_schemaSubfolderNames)
        {
            if (Directory.Exists(Path.Combine(parent, sub)))
                return parent;
        }
        return fileFolder;
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

        // Auto-expand the root TreeViewItem after Avalonia realizes its container.
        // Without this, the user has to click the chevron just to see anything —
        // fails the lazy-dev principle since the loaded contents are right there.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var firstItem = _tree.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault();
            if (firstItem is not null) firstItem.IsExpanded = true;
        }, Avalonia.Threading.DispatcherPriority.Background);
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
