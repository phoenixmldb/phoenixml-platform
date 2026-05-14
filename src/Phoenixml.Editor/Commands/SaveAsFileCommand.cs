using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Phoenixml.Editor.Commands;

internal static class SaveAsFileCommand
{
    private static readonly FilePickerFileType[] s_xmlFileTypes =
    [
        new FilePickerFileType("XML files") { Patterns = ["*.xml"] },
    ];

    public static async Task ExecuteAsync(EditorMainWindow window, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(window);

        // MVP Save-As only supports XmlDocument; text-doc save-as is Plan 27.
        if (window.State.ActiveDocument is not Phoenixml.Platform.Editor.Xml.XmlDocument doc) return;

        var topLevel = TopLevel.GetTopLevel(window.Window);
        if (topLevel is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save XML file",
            DefaultExtension = ".xml",
            FileTypeChoices = s_xmlFileTypes,
        });

        if (file is null) return;
        var path = file.TryGetLocalPath();
        if (path is null) return;

        await doc.SaveAsAsync(new Uri(path), ct);
        window.Settings.RecordRecentFile(path);
    }
}
