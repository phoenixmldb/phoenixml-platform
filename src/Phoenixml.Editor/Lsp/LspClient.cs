using System;
using System.IO;
using System.Threading.Tasks;
using StreamJsonRpc;

namespace Phoenixml.Editor.Lsp;

/// <summary>
/// Minimal LSP 3.17 client: sends <c>initialize</c>, <c>textDocument/didOpen</c>,
/// <c>textDocument/didChange</c>; receives <c>textDocument/publishDiagnostics</c>.
/// MVP scope — no hover/completion/rename/references on the client surface yet.
/// Transport-agnostic: takes pre-built input/output streams (process stdout/stdin,
/// or in-memory streams for tests).
/// </summary>
public sealed class LspClient : IAsyncDisposable
{
    private readonly JsonRpc _rpc;

    /// <summary>
    /// Creates a client bound to the given streams. The streams are typically
    /// stdin/stdout of a spawned server process, or in-memory streams in tests.
    /// </summary>
    /// <param name="sendingStream">Stream the client writes JSON-RPC to.</param>
    /// <param name="receivingStream">Stream the client reads JSON-RPC from.</param>
    public LspClient(Stream sendingStream, Stream receivingStream)
    {
        ArgumentNullException.ThrowIfNull(sendingStream);
        ArgumentNullException.ThrowIfNull(receivingStream);
        var formatter = new SystemTextJsonFormatter();
        var handler = new HeaderDelimitedMessageHandler(sendingStream, receivingStream, formatter);
        _rpc = new JsonRpc(handler, this);
        _rpc.StartListening();
    }

#pragma warning disable CA1003 // EventArgs<T> is the LSP shape we need; StreamJsonRpc requires (sender, args)
    /// <summary>Raised when the server publishes diagnostics for a document.</summary>
    public event EventHandler<LspPublishDiagnosticsParams>? DiagnosticsReceived;
#pragma warning restore CA1003

    /// <summary>Invoked by the server via JSON-RPC notification.</summary>
    [JsonRpcMethod("textDocument/publishDiagnostics")]
    public void OnPublishDiagnostics(LspPublishDiagnosticsParams p)
    {
        DiagnosticsReceived?.Invoke(this, p);
    }

    /// <summary>Performs the <c>initialize</c> handshake.</summary>
    public Task InitializeAsync() =>
        _rpc.InvokeAsync<object>("initialize", new { processId = 0, rootUri = (string?)null, capabilities = new { } });

    /// <summary>Sends the <c>initialized</c> notification after <see cref="InitializeAsync"/>.</summary>
    public Task NotifyInitializedAsync() =>
        _rpc.NotifyAsync("initialized", new { });

    /// <summary>Notifies the server that a document has been opened.</summary>
    public Task NotifyDidOpenAsync(string uri, string languageId, int version, string text)
    {
        var p = new LspDidOpenTextDocumentParams(
            new LspTextDocumentItem(uri, languageId, version, text));
        return _rpc.NotifyAsync("textDocument/didOpen", p);
    }

    /// <summary>Notifies the server that a document's text has been replaced.</summary>
    public Task NotifyDidChangeAsync(string uri, int version, string text)
    {
        var p = new LspDidChangeTextDocumentParams(
            new LspVersionedTextDocumentIdentifier(uri, version),
            [new LspTextDocumentContentChangeEvent(text)]);
        return _rpc.NotifyAsync("textDocument/didChange", p);
    }

    /// <summary>Requests completion items at <paramref name="line"/>/<paramref name="character"/>.</summary>
    public Task<LspCompletionList?> RequestCompletionAsync(string uri, int line, int character) =>
        _rpc.InvokeAsync<LspCompletionList?>("textDocument/completion",
            new LspTextDocumentPositionParams(
                new LspTextDocumentIdentifier(uri),
                new LspPosition(line, character)));

    /// <summary>Requests hover info at <paramref name="line"/>/<paramref name="character"/>.</summary>
    public Task<LspHover?> RequestHoverAsync(string uri, int line, int character) =>
        _rpc.InvokeAsync<LspHover?>("textDocument/hover",
            new LspTextDocumentPositionParams(
                new LspTextDocumentIdentifier(uri),
                new LspPosition(line, character)));

    /// <summary>Requests the document's symbol list (Plan 28).</summary>
    public Task<LspDocumentSymbol[]?> RequestDocumentSymbolsAsync(string uri) =>
        _rpc.InvokeAsync<LspDocumentSymbol[]?>("textDocument/documentSymbol",
            new LspDocumentSymbolParams(new LspTextDocumentIdentifier(uri)));

    /// <summary>Requests signature info at the given position (Plan 28).</summary>
    public Task<LspSignatureHelp?> RequestSignatureHelpAsync(string uri, int line, int character) =>
        _rpc.InvokeAsync<LspSignatureHelp?>("textDocument/signatureHelp",
            new LspTextDocumentPositionParams(
                new LspTextDocumentIdentifier(uri),
                new LspPosition(line, character)));

    /// <summary>Requests the definition location for the symbol at the given position (Plan 28).</summary>
    public Task<LspLocation?> RequestDefinitionAsync(string uri, int line, int character) =>
        _rpc.InvokeAsync<LspLocation?>("textDocument/definition",
            new LspTextDocumentPositionParams(
                new LspTextDocumentIdentifier(uri),
                new LspPosition(line, character)));

    /// <summary>Requests all reference locations for the symbol at the given position (Plan 29).</summary>
    public Task<LspLocation[]?> RequestReferencesAsync(string uri, int line, int character) =>
        _rpc.InvokeAsync<LspLocation[]?>("textDocument/references",
            new LspReferenceParams(
                new LspTextDocumentIdentifier(uri),
                new LspPosition(line, character),
                new LspReferenceContext(IncludeDeclaration: true)));

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _rpc.Dispose();
        return ValueTask.CompletedTask;
    }
}
