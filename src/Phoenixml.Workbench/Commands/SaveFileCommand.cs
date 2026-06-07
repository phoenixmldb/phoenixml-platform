using System;
using System.Threading;
using System.Threading.Tasks;
using Phoenixml.Workbench.IO;
using Phoenixml.Workbench.Settings;
using Phoenixml.Workbench.ViewModels;

namespace Phoenixml.Workbench.Commands;

/// <summary>
/// Save the active document to its existing path. Falls back to <see cref="SaveAsFileCommand"/>
/// when the document has no source URI (new / untitled document).
/// </summary>
public static class SaveFileCommand
{
    public static async Task ExecuteAsync(
        IFileDialog dialog,
        EditorState state,
        EditorSettingsStore settings,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);

        var doc = state.ActiveDocument;
        if (doc is null) return;

        if (doc.Source is null)
        {
            await SaveAsFileCommand.ExecuteAsync(dialog, state, settings, ct).ConfigureAwait(true);
            return;
        }

        await doc.SaveAsync(ct).ConfigureAwait(true);
    }
}
