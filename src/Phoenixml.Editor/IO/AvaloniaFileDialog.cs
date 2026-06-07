using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Phoenixml.Workbench.IO;

namespace Phoenixml.Editor.IO;

/// <summary>
/// Avalonia-backed <see cref="IFileDialog"/>. Translates the workbench's shell-neutral filter
/// records into <see cref="FilePickerFileType"/> and drives <see cref="IStorageProvider"/>.
/// </summary>
internal sealed class AvaloniaFileDialog : IFileDialog
{
    private readonly Func<TopLevel?> _topLevelProvider;

    /// <summary>The TopLevel is resolved lazily so the dialog can be constructed before the window opens.</summary>
    internal AvaloniaFileDialog(Func<TopLevel?> topLevelProvider)
    {
        ArgumentNullException.ThrowIfNull(topLevelProvider);
        _topLevelProvider = topLevelProvider;
    }

    public async Task<string?> OpenFileAsync(
        string title,
        IReadOnlyList<FileFilter> filters,
        CancellationToken ct)
    {
        var topLevel = _topLevelProvider();
        if (topLevel is null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = MapFilters(filters),
        }).ConfigureAwait(true);

        if (files is null || files.Count == 0) return null;
        return files[0].TryGetLocalPath();
    }

    public async Task<string?> SaveFileAsync(
        string title,
        string defaultExtension,
        IReadOnlyList<FileFilter> filters,
        CancellationToken ct)
    {
        var topLevel = _topLevelProvider();
        if (topLevel is null) return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = defaultExtension,
            FileTypeChoices = MapFilters(filters),
        }).ConfigureAwait(true);

        return file?.TryGetLocalPath();
    }

    private static FilePickerFileType[] MapFilters(IReadOnlyList<FileFilter> filters) =>
        filters.Select(f => new FilePickerFileType(f.Label) { Patterns = f.Patterns.ToList() }).ToArray();
}
