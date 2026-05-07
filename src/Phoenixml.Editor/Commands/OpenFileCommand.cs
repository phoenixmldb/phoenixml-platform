using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Phoenixml.Platform.Editor.Xml;

namespace Phoenixml.Editor.Commands;

internal static class OpenFileCommand
{
    private static readonly FilePickerFileType[] s_xmlFileTypes =
    [
        new FilePickerFileType("XML files") { Patterns = ["*.xml", "*.xsd", "*.xsl", "*.xslt"] },
        new FilePickerFileType("All files") { Patterns = ["*.*"] },
    ];

    public static async Task ExecuteAsync(EditorMainWindow window, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(window);

        var topLevel = TopLevel.GetTopLevel(window.Window);
        if (topLevel is null) return;

        // No ConfigureAwait(false) — this command mutates UI state (sets ActiveDocument
        // which synchronously refreshes views), so the awaits must resume on the UI
        // thread. Avalonia uses its own SynchronizationContext to marshal back.
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open XML file",
            AllowMultiple = false,
            FileTypeFilter = s_xmlFileTypes,
        });

        if (files is null || files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (path is null) return;

        var doc = await XmlDocument.LoadAsync(path, ct);
        window.State.OpenDocument(doc);
        window.Settings.RecordRecentFile(path);
    }
}
