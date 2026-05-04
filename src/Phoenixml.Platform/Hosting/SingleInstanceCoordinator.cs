using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Phoenixml.Platform.Hosting;

/// <summary>
/// Ensures only one instance of the application is running at a time.
/// On Windows a named pipe is used; on macOS and Linux a Unix domain socket is used.
/// If the socket or pipe infrastructure is unavailable (sandboxed environment, privilege
/// restriction), the coordinator logs the failure and returns <see langword="true"/> so the
/// application starts normally — a degraded-graceful fallback rather than a hard failure.
/// </summary>
/// <remarks>
/// Integration tests for this class are deferred; socket/pipe behaviour is
/// runtime-environment-dependent and not reliably testable in CI without
/// platform-specific setup.
/// </remarks>
public sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    private readonly string _appName;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;

    /// <summary>Initialises the coordinator for the given application.</summary>
    /// <param name="appName">The application name; used to derive the pipe or socket name.</param>
    public SingleInstanceCoordinator(string appName)
    {
        ArgumentNullException.ThrowIfNull(appName);
        _appName = appName;
    }

    /// <summary>
    /// Raised on the first instance when a second instance connects and forwards its arguments.
    /// Uses <c>Action&lt;string[]&gt;</c> for symmetry with the rest of the Abstractions event
    /// pattern (see IDocument.Changed); wrapping in EventArgs adds no value here.
    /// </summary>
#pragma warning disable CA1003 // Action<string[]> is intentional per the platform event pattern
    public event Action<string[]>? SecondInstanceLaunched;
#pragma warning restore CA1003

    /// <summary>
    /// Tries to acquire the single-instance lock.
    /// </summary>
    /// <param name="argv">The current process's command-line arguments.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if this is the first instance (or if single-instance
    /// detection could not be established — degrade gracefully);
    /// <see langword="false"/> if a second instance forwarded its arguments to the first.
    /// </returns>
    public ValueTask<bool> TryAcquireAsync(string[] argv, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(argv);
        return OperatingSystem.IsWindows()
            ? TryAcquireNamedPipeAsync(argv, ct)
            : TryAcquireUnixSocketAsync(argv, ct);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Intentional skip-on-failure: pipe infrastructure may be unavailable in sandboxed environments.")]
    private async ValueTask<bool> TryAcquireNamedPipeAsync(string[] argv, CancellationToken ct)
    {
        var pipeName = $"Phoenixml.{_appName}";
        try
        {
            // Try to connect to an existing server. If this succeeds, we are the second instance.
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            await client.ConnectAsync(200, ct).ConfigureAwait(false);
            var json = JsonSerializer.Serialize(argv);
            var bytes = Encoding.UTF8.GetBytes(json);
            await client.WriteAsync(bytes, ct).ConfigureAwait(false);
            return false;
        }
        catch (TimeoutException)
        {
            // No server — we are the first instance. Start listening.
        }
        catch (Exception)
        {
            // Degrade gracefully: if anything unexpected goes wrong, start normally.
            return true;
        }

        StartNamedPipeListener(pipeName);
        return true;
    }

    private void StartNamedPipeListener(string pipeName)
    {
        _listenerCts = new CancellationTokenSource();
        var token = _listenerCts.Token;
        _listenerTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var json = await reader.ReadToEndAsync(token).ConfigureAwait(false);
                    var args = JsonSerializer.Deserialize<string[]>(json) ?? [];
                    SecondInstanceLaunched?.Invoke(args);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
#pragma warning disable CA1031 // Intentional: swallow per-connection errors to keep the listener alive
                catch (Exception)
                {
                    // Swallow per-connection errors; keep listening.
                }
#pragma warning restore CA1031
            }
        }, token);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Intentional skip-on-failure: socket infrastructure may be unavailable in sandboxed environments.")]
    private async ValueTask<bool> TryAcquireUnixSocketAsync(string[] argv, CancellationToken ct)
    {
        var socketPath = GetUnixSocketPath();
        try
        {
            // Try to connect to an existing server.
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await client.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct).ConfigureAwait(false);
            var json = JsonSerializer.Serialize(argv);
            var bytes = Encoding.UTF8.GetBytes(json);
            await client.SendAsync(bytes, SocketFlags.None, ct).ConfigureAwait(false);
            return false;
        }
        catch (SocketException)
        {
            // No server listening — we are the first instance.
        }
        catch (Exception)
        {
            // Degrade gracefully.
            return true;
        }

        // Remove stale socket file if it exists.
        if (File.Exists(socketPath))
        {
            try { File.Delete(socketPath); }
            catch (IOException) { /* ignore — best effort */ }
        }

        StartUnixSocketListener(socketPath);
        return true;
    }

    private void StartUnixSocketListener(string socketPath)
    {
        _listenerCts = new CancellationTokenSource();
        var token = _listenerCts.Token;
        _listenerTask = Task.Run(async () =>
        {
            Socket? server = null;
            try
            {
                server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                server.Bind(new UnixDomainSocketEndPoint(socketPath));
                server.Listen(backlog: 4);

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using var conn = await server.AcceptAsync(token).ConfigureAwait(false);
                        var buf = new byte[4096];
                        var received = await conn.ReceiveAsync(buf, SocketFlags.None, token).ConfigureAwait(false);
                        var json = Encoding.UTF8.GetString(buf, 0, received);
                        var args = JsonSerializer.Deserialize<string[]>(json) ?? [];
                        SecondInstanceLaunched?.Invoke(args);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
#pragma warning disable CA1031 // Intentional: swallow per-connection errors to keep the listener alive
                    catch (Exception)
                    {
                        // Swallow per-connection errors.
                    }
#pragma warning restore CA1031
                }
            }
#pragma warning disable CA1031 // Intentional: listener setup failure is non-fatal; app already launched
            catch (Exception)
            {
                // Listener setup failed; already degraded gracefully at TryAcquire time.
            }
#pragma warning restore CA1031
            finally
            {
                server?.Dispose();
                try { File.Delete(socketPath); }
                catch (IOException) { /* ignore */ }
            }
        }, token);
    }

    private string GetUnixSocketPath()
    {
        var dir = SettingsService.GetPlatformSettingsRoot();
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{_appName}.sock");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_listenerCts is not null)
        {
            await _listenerCts.CancelAsync().ConfigureAwait(false);
            if (_listenerTask is not null)
            {
                try { await _listenerTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { /* expected */ }
            }
            _listenerCts.Dispose();
        }
    }
}
