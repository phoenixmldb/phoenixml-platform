using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Phoenixml.Platform.Editor.Text;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Workbench.IO;
using Phoenixml.Workbench.Settings;
using Phoenixml.Workbench.ViewModels;

namespace Phoenixml.Workbench.Commands;

/// <summary>
/// Show the Open File picker, load the chosen file as the right Document subtype, and add it
/// to the workbench as a new tab. Records the chosen path in recent files.
/// </summary>
public static class OpenFileCommand
{
    private static readonly FileFilter[] s_filters =
    [
        new FileFilter("XML files", ["*.xml", "*.xsd"]),
        new FileFilter("XSLT",      ["*.xsl", "*.xslt"]),
        new FileFilter("XQuery",    ["*.xq", "*.xqy", "*.xquery", "*.xqm"]),
        new FileFilter("All files", ["*.*"]),
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

        var path = await dialog.OpenFileAsync("Open file", s_filters, ct).ConfigureAwait(true);
        if (path is null) return;

        // Route by extension: XQuery/XSLT open as TextDocument (LSP-driven), everything else as XmlDocument.
        // ToLowerInvariant is fine — file extensions are matched as lowercase tokens, not displayed.
#pragma warning disable CA1308
        var ext = Path.GetExtension(path).ToLowerInvariant();
#pragma warning restore CA1308
        Phoenixml.Platform.Documents.Document doc;
        if (ext is ".xq" or ".xqy" or ".xquery" or ".xqm" or ".xsl" or ".xslt")
        {
            doc = await TextDocument.LoadAsync(path, ct).ConfigureAwait(true);
        }
        else
        {
            doc = await XmlDocument.LoadAsync(path, ct).ConfigureAwait(true);
        }

        state.OpenDocument(doc);
        settings.RecordRecentFile(path);
    }
}
