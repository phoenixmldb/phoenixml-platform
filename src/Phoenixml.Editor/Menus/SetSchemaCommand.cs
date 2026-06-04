using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Phoenixml.Platform.Editor.Xml;

namespace Phoenixml.Editor.Menus;

/// <summary>
/// Implements the <c>File → Set Schema…</c> menu command.
/// </summary>
/// <remarks>
/// Opens an XSD file picker for the active document. On confirmation the chosen path is
/// persisted into <see cref="Phoenixml.Workbench.Settings.EditorSettings.PerDocumentSchema"/> via
/// <see cref="Phoenixml.Workbench.Settings.EditorSettingsStore.SetPerDocumentSchema"/>, then an immediate
/// re-validation pass is triggered via
/// <see cref="Validation.EditorValidationService.Revalidate"/> so diagnostics refresh
/// without the user having to make a document edit.
/// </remarks>
internal static class SetSchemaCommand
{
    private static readonly FilePickerFileType[] s_xsdTypes =
    [
        new FilePickerFileType("XSD schemas") { Patterns = ["*.xsd"] },
        new FilePickerFileType("All files") { Patterns = ["*.*"] },
    ];

    /// <summary>
    /// Executes the Set Schema command.
    /// </summary>
    /// <remarks>
    /// No-ops silently when there is no active document, when the active document has no
    /// file-system source URI, or when the user cancels the file picker.
    /// </remarks>
    /// <param name="window">The editor main window.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task ExecuteAsync(EditorMainWindow window, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.State.ActiveDocument is not XmlDocument doc) return;
        if (doc.Source is not Uri source) return;

        var topLevel = TopLevel.GetTopLevel(window.Window);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Choose schema for {doc.DisplayName}",
            AllowMultiple = false,
            FileTypeFilter = s_xsdTypes,
        });

        if (files is null || files.Count == 0) return;
        if (files[0].TryGetLocalPath() is not string xsdPath) return;

        window.Settings.SetPerDocumentSchema(source.LocalPath, xsdPath);
        window.Validation.Revalidate();  // immediate re-validation, don't wait for Document.Changed
    }
}
