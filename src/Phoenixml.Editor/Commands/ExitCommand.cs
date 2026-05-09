using Avalonia.Controls;

namespace Phoenixml.Editor.Commands;

internal static class ExitCommand
{
    public static void Execute(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Close();
    }
}
