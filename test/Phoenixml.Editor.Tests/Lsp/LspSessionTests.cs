using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Phoenixml.Workbench.Lsp;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Editor.Text;
using StreamJsonRpc;
using Xunit;

namespace Phoenixml.Editor.Tests.Lsp;

public class LspSessionTests
{
    /// <summary>Stub server: records didOpen/didChange and can push back diagnostics.</summary>
    private sealed class StubServer
    {
        public JsonRpc? Rpc { get; set; }
        public string? LastDidOpenUri { get; private set; }
        public int LastDidOpenVersion { get; private set; }
        public int LastDidChangeVersion { get; private set; }

        [JsonRpcMethod("initialize")]
        public object Initialize(object? _) => new { capabilities = new { textDocumentSync = 1 } };

        [JsonRpcMethod("initialized")]
        public void Initialized(object? _) { }

        [JsonRpcMethod("textDocument/didOpen")]
        public async Task DidOpen(LspDidOpenTextDocumentParams p)
        {
            LastDidOpenUri = p.TextDocument.Uri;
            LastDidOpenVersion = p.TextDocument.Version;
            if (Rpc is not null)
            {
                await Rpc.NotifyAsync("textDocument/publishDiagnostics",
                    new LspPublishDiagnosticsParams(p.TextDocument.Uri,
                        [new LspDiagnostic(
                            new LspRange(new LspPosition(0, 0), new LspPosition(0, 1)),
                            Severity: 1, Code: "STUB", Source: "stub",
                            Message: "stub diag")])).ConfigureAwait(false);
            }
        }

        [JsonRpcMethod("textDocument/didChange")]
        public void DidChange(LspDidChangeTextDocumentParams p)
        {
            LastDidChangeVersion = p.TextDocument.Version;
        }
    }

    private static (LspSession session, StubServer server, TextDocument doc) BuildSession()
    {
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

        var doc = new TextDocument(DocumentId.New(), new Uri("file:///test.xq"), "test.xq", DocumentKind.XQuery, "1 + 1");
        var session = new LspSession(doc,
            sendingStream: clientToServer.Writer.AsStream(),
            receivingStream: serverToClient.Reader.AsStream());
        return (session, server, doc);
    }

    [Fact]
    public async Task StartSendsInitializeAndDidOpen()
    {
        var (session, server, _) = BuildSession();
        await session.StartAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Equal("file:///test.xq", server.LastDidOpenUri);
        Assert.Equal(1, server.LastDidOpenVersion);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task DocumentReplaceAllTriggersDidChange()
    {
        var (session, server, doc) = BuildSession();
        await session.StartAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        doc.ReplaceAll("changed");
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Equal(2, server.LastDidChangeVersion);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task ServerDiagnosticsRaiseDiagnosticsArrived()
    {
        var (session, _, _) = BuildSession();
        IReadOnlyList<EditorLspDiagnostic>? received = null;
        var tcs = new TaskCompletionSource();
        session.DiagnosticsArrived += (_, d) =>
        {
            received = d;
            tcs.TrySetResult();
        };

        await session.StartAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.NotNull(received);
        Assert.NotEmpty(received!);
        Assert.Equal("stub diag", received[0].Message);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task DisposeStopsForwardingEvents()
    {
        var (session, _, doc) = BuildSession();
        await session.StartAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var afterDispose = 0;
        session.DiagnosticsArrived += (_, _) => afterDispose++;

        await session.DisposeAsync();
        doc.ReplaceAll("post-dispose");
        await Task.Delay(150, TestContext.Current.CancellationToken);

        Assert.Equal(0, afterDispose);
    }
}
