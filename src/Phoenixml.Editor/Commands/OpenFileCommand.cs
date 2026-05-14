using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Phoenixml.Platform.Editor.Text;
using Phoenixml.Platform.Editor.Xml;

namespace Phoenixml.Editor.Commands;

internal static class OpenFileCommand
{
    private static readonly FilePickerFileType[] s_fileTypes =
    [
        new FilePickerFileType("XML files") { Patterns = ["*.xml", "*.xsd"] },
        new FilePickerFileType("XSLT") { Patterns = ["*.xsl", "*.xslt"] },
        new FilePickerFileType("XQuery") { Patterns = ["*.xq", "*.xqy", "*.xquery", "*.xqm"] },
        new FilePickerFileType("All files") { Patterns = ["*.*"] },
    ];

    public static async Task ExecuteAsync(EditorMainWindow window, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(window);

        var topLevel = TopLevel.GetTopLevel(window.Window);
        if (topLevel is null) return;

        // No ConfigureAwait(false) — this command mutates UI state, so awaits must
        // resume on the Avalonia UI SynchronizationContext.
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open file",
            AllowMultiple = false,
            FileTypeFilter = s_fileTypes,
        });

        if (files is null || files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (path is null) return;

        // Plan 26: route by extension. XQuery/XSLT files open as TextDocument
        // (plain text + LSP-driven diagnostics). Everything else stays XmlDocument.
        var ext = Path.GetExtension(path).ToLowerInvariant();
        Phoenixml.Platform.Documents.Document doc;
        if (ext is ".xq" or ".xqy" or ".xquery" or ".xqm" or ".xsl" or ".xslt")
        {
            doc = await TextDocument.LoadAsync(path, ct);
        }
        else
        {
            doc = await XmlDocument.LoadAsync(path, ct);
        }
        window.State.OpenDocument(doc);
        window.Settings.RecordRecentFile(path);
    }
}
