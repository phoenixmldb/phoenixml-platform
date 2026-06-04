using System;
using System.Diagnostics;
using System.IO;

namespace Phoenixml.Workbench.Lsp;

/// <summary>
/// Wraps <see cref="Process"/> for an LSP server child process. Exposes the child's
/// stdin/stdout as <see cref="Stream"/>s for direct use by <see cref="LspClient"/>.
/// Dispose kills the process tree (best-effort).
/// </summary>
public sealed class LspServerProcess : IDisposable
{
    private readonly Process _proc;
    private bool _disposed;

    /// <summary>Spawns the LSP server binary at <paramref name="exePath"/>.</summary>
    public LspServerProcess(string exePath)
    {
        ArgumentNullException.ThrowIfNull(exePath);
        _proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };
        _proc.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
        _proc.Start();
    }

#pragma warning disable CA1003
    /// <summary>Raised when the child process exits (intentional or otherwise).</summary>
    public event EventHandler? Exited;
#pragma warning restore CA1003

    /// <summary>The child process's stdin stream — writes here are sent to the server.</summary>
    public Stream StandardInput => _proc.StandardInput.BaseStream;

    /// <summary>The child process's stdout stream — server messages arrive here.</summary>
    public Stream StandardOutput => _proc.StandardOutput.BaseStream;

    /// <summary>Whether the child process is still running.</summary>
    public bool IsRunning => !_disposed && !_proc.HasExited;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (!_proc.HasExited)
            {
                _proc.Kill(entireProcessTree: true);
                _proc.WaitForExit(2000);
            }
        }
        catch (InvalidOperationException) { /* already exited */ }
        catch (System.ComponentModel.Win32Exception) { /* permission */ }
        _proc.Dispose();
    }
}
