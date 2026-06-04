using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Editor.Text;

namespace Phoenixml.Workbench.Lsp;

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

    /// <summary>
    /// Raised alongside <see cref="DiagnosticsArrived"/> with the raw LSP payload —
    /// used by callers (e.g. <c>EditorMainWindow</c>) that convert to
    /// <see cref="Phoenixml.Platform.Abstractions.Editing.IDiagnostic"/> for ProblemsView.
    /// </summary>
    public event EventHandler<LspPublishDiagnosticsParams>? RawDiagnosticsArrived;
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
        RawDiagnosticsArrived?.Invoke(this, p);
        var converted = LspDiagnosticBridge.Convert(p);
        DiagnosticsArrived?.Invoke(this, converted);
    }

    /// <summary>Requests completion labels at the given LSP-0-based position.</summary>
    public async Task<IReadOnlyList<string>> RequestCompletionAsync(int line, int character)
    {
        var uri = _doc.Source?.AbsoluteUri ?? $"untitled:{_doc.Id}";
        try
        {
            var list = await _client.RequestCompletionAsync(uri, line, character).ConfigureAwait(false);
            if (list is null) return Array.Empty<string>();
            var labels = new string[list.Items.Length];
            for (int i = 0; i < list.Items.Length; i++) labels[i] = list.Items[i].Label;
            return labels;
        }
        catch (StreamJsonRpc.RemoteRpcException) { return Array.Empty<string>(); }
    }

    /// <summary>Requests hover text at the given position. Null when no info.</summary>
    public async Task<string?> RequestHoverAsync(int line, int character)
    {
        var uri = _doc.Source?.AbsoluteUri ?? $"untitled:{_doc.Id}";
        try
        {
            var hover = await _client.RequestHoverAsync(uri, line, character).ConfigureAwait(false);
            return hover?.Contents;
        }
        catch (StreamJsonRpc.RemoteRpcException) { return null; }
    }

    /// <summary>Requests the document's top-level symbols (Plan 28).</summary>
    public async Task<IReadOnlyList<LspDocumentSymbol>> RequestDocumentSymbolsAsync()
    {
        var uri = _doc.Source?.AbsoluteUri ?? $"untitled:{_doc.Id}";
        try
        {
            var symbols = await _client.RequestDocumentSymbolsAsync(uri).ConfigureAwait(false);
            return symbols ?? Array.Empty<LspDocumentSymbol>();
        }
        catch (StreamJsonRpc.RemoteRpcException) { return Array.Empty<LspDocumentSymbol>(); }
    }

    /// <summary>Requests signature help at the given position (Plan 28).</summary>
    public async Task<LspSignatureHelp?> RequestSignatureHelpAsync(int line, int character)
    {
        var uri = _doc.Source?.AbsoluteUri ?? $"untitled:{_doc.Id}";
        try
        {
            return await _client.RequestSignatureHelpAsync(uri, line, character).ConfigureAwait(false);
        }
        catch (StreamJsonRpc.RemoteRpcException) { return null; }
    }

    /// <summary>Requests the definition location at the given position (Plan 28).</summary>
    public async Task<LspLocation?> RequestDefinitionAsync(int line, int character)
    {
        var uri = _doc.Source?.AbsoluteUri ?? $"untitled:{_doc.Id}";
        try
        {
            return await _client.RequestDefinitionAsync(uri, line, character).ConfigureAwait(false);
        }
        catch (StreamJsonRpc.RemoteRpcException) { return null; }
    }

    /// <summary>Requests all references for the symbol at the given position (Plan 29).</summary>
    public async Task<LspLocation[]> RequestReferencesAsync(int line, int character)
    {
        var uri = _doc.Source?.AbsoluteUri ?? $"untitled:{_doc.Id}";
        try
        {
            var refs = await _client.RequestReferencesAsync(uri, line, character).ConfigureAwait(false);
            return refs ?? Array.Empty<LspLocation>();
        }
        catch (StreamJsonRpc.RemoteRpcException) { return Array.Empty<LspLocation>(); }
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
