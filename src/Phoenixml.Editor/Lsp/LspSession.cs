using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Editor.Text;

namespace Phoenixml.Editor.Lsp;

/// <summary>
/// Per-document LSP session. Couples a <see cref="TextDocument"/> to an
/// <see cref="LspClient"/> backed by an <see cref="LspServerProcess"/>. On
/// <see cref="StartAsync"/>: sends initialize + initialized + didOpen.
/// On <see cref="TextDocument"/> changes: sends didChange. On server diagnostics:
/// emits <see cref="DiagnosticsArrived"/> with converted records.
/// </summary>
public sealed class LspSession : IAsyncDisposable
{
    private readonly TextDocument _doc;
    private readonly LspServerProcess _process;
    private readonly LspClient _client;
    private readonly string _languageId;
    private bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="LspSession"/> with pre-built streams. Used by
    /// production callers via <see cref="LspServerProcess"/> and by tests via
    /// in-memory pipes.
    /// </summary>
    public LspSession(TextDocument doc, Stream sendingStream, Stream receivingStream, LspServerProcess? process = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(sendingStream);
        ArgumentNullException.ThrowIfNull(receivingStream);
        _doc = doc;
        _process = process!;  // null acceptable for tests; production always supplies one
        _client = new LspClient(sendingStream, receivingStream);
        _languageId = doc.Kind switch
        {
            DocumentKind.XQuery => "xquery",
            DocumentKind.Xslt => "xsl",
            _ => "plaintext",
        };
        _client.DiagnosticsReceived += OnDiagnostics;
        _doc.Changed += OnDocumentChanged;
    }

    /// <summary>Convenience overload that wraps an <see cref="LspServerProcess"/>.</summary>
    public static LspSession FromProcess(TextDocument doc, LspServerProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return new LspSession(doc, process.StandardInput, process.StandardOutput, process);
    }

#pragma warning disable CA1003
    /// <summary>Raised when the server publishes diagnostics for this document.</summary>
    public event EventHandler<IReadOnlyList<EditorLspDiagnostic>>? DiagnosticsArrived;
#pragma warning restore CA1003

    /// <summary>Performs the LSP handshake + opens the document on the server.</summary>
    public async Task StartAsync()
    {
        await _client.InitializeAsync().ConfigureAwait(false);
        await _client.NotifyInitializedAsync().ConfigureAwait(false);
        var uri = _doc.Source?.AbsoluteUri ?? $"untitled:{_doc.Id}";
        await _client.NotifyDidOpenAsync(uri, _languageId, _doc.Version, _doc.Text).ConfigureAwait(false);
    }

    private void OnDocumentChanged(DocumentChange change)
    {
        _ = change;
        if (_disposed) return;
        var uri = _doc.Source?.AbsoluteUri ?? $"untitled:{_doc.Id}";
        _ = _client.NotifyDidChangeAsync(uri, _doc.Version, _doc.Text);
    }

    private void OnDiagnostics(object? sender, LspPublishDiagnosticsParams p)
    {
        if (_disposed) return;
        var converted = LspDiagnosticBridge.Convert(p);
        DiagnosticsArrived?.Invoke(this, converted);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _doc.Changed -= OnDocumentChanged;
        _client.DiagnosticsReceived -= OnDiagnostics;
        await _client.DisposeAsync().ConfigureAwait(false);
        _process?.Dispose();
    }
}
