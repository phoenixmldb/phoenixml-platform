namespace Phoenixml.Workbench.Shell;

/// <summary>
/// Shell-neutral handle to the host application. Lets workbench commands trigger top-level
/// actions (exit, etc.) without referencing a specific UI framework.
/// </summary>
public interface IShellHost
{
    /// <summary>Close the workbench (close the main window / quit the process).</summary>
    void RequestExit();
}
