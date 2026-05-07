using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Phoenixml.Editor.Explorer;

/// <summary>
/// View-model for one folder or file row in the <see cref="EditorExplorer"/>.
/// Folders lazily populate <see cref="Children"/> on first expansion so the explorer
/// doesn't freeze on deep trees.
/// </summary>
internal sealed class ExplorerNode
{
    private static readonly string[] s_xmlExtensions = [".xml", ".xsd", ".xsl", ".xslt", ".sch"];

    private bool _childrenLoaded;

    public string Path { get; }
    public string DisplayName { get; }
    public bool IsDirectory { get; }

    public ObservableCollection<ExplorerNode> Children { get; } = [];

    public ExplorerNode(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = path;
        var name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        DisplayName = string.IsNullOrEmpty(name) ? path : name;
        IsDirectory = Directory.Exists(path);

        if (IsDirectory)
        {
            // Add a placeholder so the tree shows an expand chevron without enumerating yet.
            Children.Add(s_placeholder);
        }
    }

    private static readonly ExplorerNode s_placeholder = new("<placeholder>", isPlaceholder: true);

    private ExplorerNode(string path, bool isPlaceholder)
    {
        Path = path;
        DisplayName = "(loading…)";
        IsDirectory = false;
        _childrenLoaded = !isPlaceholder;
    }

    /// <summary>
    /// Populates <see cref="Children"/> with subfolders containing XML files plus the XML files
    /// themselves. Idempotent — repeated calls are no-ops after the first.
    /// </summary>
    public void EnsureChildrenLoaded()
    {
        if (_childrenLoaded || !IsDirectory) return;
        _childrenLoaded = true;
        Children.Clear();

        try
        {
            var subdirs = Directory.EnumerateDirectories(Path)
                .Where(ContainsXmlContent)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            foreach (var dir in subdirs)
                Children.Add(new ExplorerNode(dir));

            var files = Directory.EnumerateFiles(Path)
                .Where(IsXmlFile)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
                Children.Add(new ExplorerNode(file));
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    private static bool IsXmlFile(string path) =>
        s_xmlExtensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Quick check: does <paramref name="dir"/> contain any XML file (anywhere in its subtree)?
    /// Stops at the first hit. Folders without XML content are hidden from the explorer to
    /// reduce noise — most editing tasks only touch a handful of file types.
    /// </summary>
    private static bool ContainsXmlContent(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir))
                if (IsXmlFile(f)) return true;
            foreach (var sub in Directory.EnumerateDirectories(dir))
                if (ContainsXmlContent(sub)) return true;
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        return false;
    }
}
