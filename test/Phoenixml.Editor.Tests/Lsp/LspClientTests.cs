using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Phoenixml.Editor.Lsp;
using StreamJsonRpc;
using Xunit;

namespace Phoenixml.Editor.Tests.Lsp;

public class LspClientTests
{
    /// <summary>
    /// Stub LSP server target that lives in-process. Mirrors the real server's contract
    /// so we can validate the client's wire format end-to-end without spawning a process.
    /// </summary>
    private sealed class StubServer
    {
        public JsonRpc? Rpc { get; set; }
        public string? LastDidOpenUri { get; private set; }
        public int LastDidOpenVersion { get; private set; }
        public string? LastDidChangeText { get; private set; }

        [JsonRpcMethod("initialize")]
        public object Initialize(object? _) => new { capabilities = new { textDocumentSync = 1 } };

        [JsonRpcMethod("initialized")]
        public void Initialized(object? _) { }

        [JsonRpcMethod("textDocument/didOpen")]
        public async Task DidOpen(LspDidOpenTextDocumentParams p)
        {
            LastDidOpenUri = p.TextDocument.Uri;
            LastDidOpenVersion = p.TextDocument.Version;
            // Echo back a diagnostic so the client can verify the receive path.
            if (Rpc is not null)
            {
                await Rpc.NotifyAsync("textDocument/publishDiagnostics",
                    new LspPublishDiagnosticsParams(p.TextDocument.Uri,
                        [new LspDiagnostic(
                            new LspRange(new LspPosition(0, 0), new LspPosition(0, 1)),
                            Severity: 1, Code: "TEST", Source: "stub",
                            Message: "stub diagnostic")])).ConfigureAwait(false);
            }
        }

        [JsonRpcMethod("textDocument/didChange")]
        public void DidChange(LspDidChangeTextDocumentParams p)
        {
            LastDidChangeText = p.ContentChanges.Length > 0
                ? p.ContentChanges[^1].Text
                : null;
        }
    }

    private static (LspClient client, StubServer server) BuildPair()
    {
        // Two duplex pipe-pairs simulate the bidirectional channel between client and server.
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var server = new StubServer();
        var serverFormatter = new SystemTextJsonFormatter();
        var serverHandler = new HeaderDelimitedMessageHandler(
            sendingStream: serverToClient.Writer.AsStream(),
            receivingStream: clientToServer.Reader.AsStream(),
            serverFormatter);
        var serverRpc = new JsonRpc(serverHandler, server);
        server.Rpc = serverRpc;
        serverRpc.StartListening();

        var client = new LspClient(
            sendingStream: clientToServer.Writer.AsStream(),
            receivingStream: serverToClient.Reader.AsStream());
        return (client, server);
    }

    [Fact]
    public async Task InitializeRoundTrips()
    {
        var (client, _) = BuildPair();
        await client.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await client.NotifyInitializedAsync();
        await client.DisposeAsync();
    }

    [Fact]
    public async Task DidOpenSendsCorrectFields()
    {
        var (client, server) = BuildPair();
        await client.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await client.NotifyDidOpenAsync("file:///x.xq", "xquery", 1, "1 + 1");
        // Allow the notification to drain.
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Equal("file:///x.xq", server.LastDidOpenUri);
        Assert.Equal(1, server.LastDidOpenVersion);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task ClientReceivesPublishedDiagnostics()
    {
        var (client, _) = BuildPair();
        LspPublishDiagnosticsParams? received = null;
        var tcs = new TaskCompletionSource();
        client.DiagnosticsReceived += (_, p) =>
        {
            received = p;
            tcs.TrySetResult();
        };

        await client.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await client.NotifyDidOpenAsync("file:///y.xq", "xquery", 1, "1+");
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.NotNull(received);
        Assert.Equal("file:///y.xq", received!.Uri);
        Assert.NotEmpty(received.Diagnostics);
        Assert.Equal("stub diagnostic", received.Diagnostics[0].Message);

        await client.DisposeAsync();
    }
}
