namespace Phoenixml.Editor.Commands;

internal static class SaveFileCommand
{
    public static async Task ExecuteAsync(EditorMainWindow window, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(window);

        var doc = window.State.ActiveDocument;
        if (doc is null) return;

        if (doc.Source is null)
        {
            await SaveAsFileCommand.ExecuteAsync(window, ct).ConfigureAwait(false);
            return;
        }

        await doc.SaveAsync(ct).ConfigureAwait(false);
    }
}
