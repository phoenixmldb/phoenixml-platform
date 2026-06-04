namespace Phoenixml.Editor.Commands;

public static class SaveFileCommand
{
    public static async Task ExecuteAsync(EditorMainWindow window, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(window);

        var doc = window.State.ActiveDocument;
        if (doc is null) return;

        if (doc.Source is null)
        {
            await SaveAsFileCommand.ExecuteAsync(window, ct);
            return;
        }

        await doc.SaveAsync(ct);
    }
}
