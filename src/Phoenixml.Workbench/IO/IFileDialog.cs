using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenixml.Workbench.IO;

/// <summary>
/// Shell-neutral file picker. The workbench drives Open/SaveAs flows through this interface
/// so commands stay free of any UI framework. Returning <c>null</c> means the user cancelled.
/// </summary>
public interface IFileDialog
{
    /// <summary>Show an open-file picker and return the chosen local path, or <c>null</c> if cancelled.</summary>
    Task<string?> OpenFileAsync(string title, IReadOnlyList<FileFilter> filters, CancellationToken ct);

    /// <summary>Show a save-as picker and return the chosen local path, or <c>null</c> if cancelled.</summary>
    Task<string?> SaveFileAsync(
        string title,
        string defaultExtension,
        IReadOnlyList<FileFilter> filters,
        CancellationToken ct);
}
