using System;
using Phoenixml.Workbench.Shell;

namespace Phoenixml.Workbench.Commands;

/// <summary>
/// Exit the application by asking the shell host to close. The host decides whether to prompt
/// for unsaved changes, close the window, or quit the process.
/// </summary>
public static class ExitCommand
{
    public static void Execute(IShellHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.RequestExit();
    }
}
