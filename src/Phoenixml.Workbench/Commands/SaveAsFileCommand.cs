using System;
using System.Threading;
using System.Threading.Tasks;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Workbench.IO;
using Phoenixml.Workbench.Settings;
using Phoenixml.Workbench.ViewModels;

namespace Phoenixml.Workbench.Commands;

/// <summary>
/// Show the Save As picker and write the active document to the chosen path.
/// MVP scope: XmlDocument only (text-document save-as is Plan 27).
/// </summary>
public static class SaveAsFileCommand
{
    private static readonly FileFilter[] s_xmlFilters =
    [
        new FileFilter("XML files", ["*.xml"]),
    ];

    public static async Task ExecuteAsync(
        IFileDialog dialog,
        EditorState state,
        EditorSettingsStore settings,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);

        if (state.ActiveDocument is not XmlDocument doc) return;

        var path = await dialog.SaveFileAsync("Save XML file", ".xml", s_xmlFilters, ct).ConfigureAwait(true);
        if (path is null) return;

        await doc.SaveAsAsync(new Uri(path), ct).ConfigureAwait(true);
        settings.RecordRecentFile(path);
    }
}
