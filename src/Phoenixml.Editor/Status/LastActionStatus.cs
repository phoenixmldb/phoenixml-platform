using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Phoenixml.Editor.Status;

/// <summary>
/// Transient status-bar notification with TTL auto-clear. Set by editor commands when they
/// want to surface a silent reject / success message in the <c>last-action</c> status segment
/// without disturbing the rest of the UI.
/// </summary>
/// <remarks>
/// The status auto-clears <see cref="TimeSpan"/> after the most recent <see cref="Notify"/>.
/// Setting a new message while an earlier one is still visible resets the timer.
/// <see cref="Changed"/> fires on both notify and auto-clear; the auto-clear callback marshals
/// to the UI thread.
/// </remarks>
public sealed class LastActionStatus
{
    private readonly TimeSpan _ttl;
    private CancellationTokenSource? _cts;

    /// <summary>Initializes a new <see cref="LastActionStatus"/> with the given TTL.</summary>
    public LastActionStatus(TimeSpan autoClearAfter)
    {
        _ttl = autoClearAfter;
    }

    /// <summary>The current message, or <see langword="null"/> when nothing is being shown.</summary>
    public string? Message { get; private set; }

#pragma warning disable CA1003 // Action design matches existing FindBar/XPathBar event style
    /// <summary>Fires when <see cref="Message"/> changes (notify or auto-clear).</summary>
    public event Action? Changed;
#pragma warning restore CA1003

    /// <summary>Sets <see cref="Message"/> and (re)starts the auto-clear timer.</summary>
    public void Notify(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        Message = message;
        Changed?.Invoke();

        var ct = _cts.Token;
        _ = ScheduleClearAsync(ct);
    }

    private async Task ScheduleClearAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(_ttl, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Message = null;
            Changed?.Invoke();
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                Message = null;
                Changed?.Invoke();
            });
        }
    }
}
