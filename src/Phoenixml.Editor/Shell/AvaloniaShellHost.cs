using System;
using Avalonia.Controls;
using Phoenixml.Workbench.Shell;

namespace Phoenixml.Editor.Shell;

/// <summary>
/// Avalonia-backed <see cref="IShellHost"/>. Closing the main window terminates the editor
/// process via Avalonia's default lifetime behavior.
/// </summary>
internal sealed class AvaloniaShellHost : IShellHost
{
    private readonly Window _window;

    internal AvaloniaShellHost(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
    }

    public void RequestExit() => _window.Close();
}
