using System.Collections.Generic;
using PhoenixmlDb.Core;
using Phoenixml.Workbench.Find;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Tree;
using Xunit;
using PlatformDocumentId = Phoenixml.Platform.Abstractions.Documents.DocumentId;

namespace Phoenixml.Editor.Tests.Find;

/// <summary>
/// Unit tests for <see cref="FindController"/> replace operations:
/// <see cref="FindController.OnReplaceCurrent"/> and <see cref="FindController.OnReplaceAll"/>.
/// Uses the same <see cref="IFindHost"/> stub pattern as <see cref="FindControllerTests"/>.
/// </summary>
public class FindControllerReplaceTests
{
    // -------------------------------------------------------------------------
    // Fixture XML
    // -------------------------------------------------------------------------

    /// <summary>XML with two &lt;item&gt; elements whose text content contains "foo".</summary>
    private const string ItemsXml = """
        <root>
          <item>foo bar</item>
          <item>foo baz</item>
        </root>
        """;

    private static XmlDocument MakeDoc(string xml)
    {
        var (result, nsTable) = XmlDocument.ParseXml(xml);
        return new XmlDocument(PlatformDocumentId.New(), source: null, "test.xml", result, nsTable);
    }

    // -------------------------------------------------------------------------
    // Stub IFindHost
    // -------------------------------------------------------------------------

    private sealed class StubFindHost : IFindHost
    {
        private readonly XmlDocument? _doc;

        public StubFindHost(XmlDocument? doc = null)
        {
            _doc = doc;
            if (_doc is not null)
                ActiveProvider = _doc.TreeNodes as XmlTreeNodeProvider;
        }

        /// <inheritdoc/>
        public XmlDocument? ActiveDocument => _doc;

        /// <inheritdoc/>
        public XmlTreeNodeProvider? ActiveProvider { get; }

        /// <summary>The most recent <c>EnsureVisibleAndSelect</c> nodeId argument, or null.</summary>
        public string? LastSelectedNodeId { get; private set; }

        /// <summary>How many times <c>HideFindBar</c> was called.</summary>
        public int HideFindBarCallCount { get; private set; }

        /// <summary>The sequence of (active, total) pairs passed to <c>SetMatchCount</c>.</summary>
        public List<(int Active, int Total)> MatchCountCalls { get; } = [];

        /// <inheritdoc/>
        public void EnsureVisibleAndSelect(string nodeId)
        {
            ArgumentNullException.ThrowIfNull(nodeId);
            LastSelectedNodeId = nodeId;
        }

        /// <inheritdoc/>
        public void HideFindBar() => HideFindBarCallCount++;

        /// <inheritdoc/>
        public void SetMatchCount(int active, int total) => MatchCountCalls.Add((active, total));
    }

    private static (FindController controller, StubFindHost host, MatchIndex index) Make(
        XmlDocument? doc = null)
    {
        var host = new StubFindHost(doc);
        var index = new MatchIndex();
        var controller = new FindController(host, index);
        return (controller, host, index);
    }

    // -------------------------------------------------------------------------
    // OnReplaceCurrent tests
    // -------------------------------------------------------------------------

    [Fact]
    public void OnReplaceCurrentExecutesSingleCommandThroughUndoStack()
    {
        var doc = MakeDoc(ItemsXml);
        var (controller, _, _) = Make(doc);

        controller.OnQueryChanged("foo", matchCase: false);
        // Undo stack starts empty; after replace there should be one entry.
        Assert.False(doc.Undo.CanUndo);

        controller.OnReplaceCurrent("bar");

        Assert.True(doc.Undo.CanUndo);
    }

    [Fact]
    public void OnReplaceCurrentNoActiveDoesNothing()
    {
        var doc = MakeDoc(ItemsXml);
        var (controller, _, _) = Make(doc);

        // Do not call OnQueryChanged — no active match.
        controller.OnReplaceCurrent("bar");

        Assert.False(doc.Undo.CanUndo);
    }

    [Fact]
    public void OnReplaceCurrentNoDocumentDoesNothing()
    {
        // No document in host.
        var (controller, _, _) = Make(doc: null);

        controller.OnReplaceCurrent("bar");

        // No exception; nothing to assert beyond it not throwing.
    }

    // -------------------------------------------------------------------------
    // OnReplaceAll tests
    // -------------------------------------------------------------------------

    [Fact]
    public void OnReplaceAllWrapsInCompoundCommand()
    {
        var doc = MakeDoc(ItemsXml);
        var (controller, _, _) = Make(doc);

        controller.OnQueryChanged("foo", matchCase: false);
        controller.OnReplaceAll("baz");

        // One compound on the undo stack (single Ctrl+Z reverts all replacements).
        Assert.True(doc.Undo.CanUndo);
        Assert.False(doc.Undo.CanRedo);
    }

    [Fact]
    public void OnReplaceAllUndoRevertsAllInOneStep()
    {
        var doc = MakeDoc(ItemsXml);
        var (controller, _, _) = Make(doc);

        controller.OnQueryChanged("foo", matchCase: false);
        var matchCountBefore = doc.Undo.UndoCommands.Count;
        controller.OnReplaceAll("baz");

        // One new undo entry for the whole batch.
        Assert.Equal(matchCountBefore + 1, doc.Undo.UndoCommands.Count);

        // Undo the batch — undo stack should return to its prior state.
        doc.Undo.Undo();
        Assert.Equal(matchCountBefore, doc.Undo.UndoCommands.Count);
        Assert.True(doc.Undo.CanRedo);
    }

    [Fact]
    public void OnReplaceAllOnEmptyMatchListIsNoOp()
    {
        var doc = MakeDoc(ItemsXml);
        var (controller, _, _) = Make(doc);

        controller.OnQueryChanged("zzznomatch", matchCase: false);
        controller.OnReplaceAll("baz");

        Assert.False(doc.Undo.CanUndo);
    }

    // -------------------------------------------------------------------------
    // Refresh-after-replace test
    // -------------------------------------------------------------------------

    [Fact]
    public void RefreshMatchesAfterReplaceRecomputes()
    {
        var doc = MakeDoc(ItemsXml);
        var (controller, host, index) = Make(doc);

        controller.OnQueryChanged("foo", matchCase: false);
        // Two "foo" text nodes matched initially.
        int initialTotal = index.Matches.Count;
        Assert.True(initialTotal > 0);

        // Replace the currently active match.
        controller.OnReplaceCurrent("replaced");

        // After the replace, Document.Changed fires, triggering RefreshMatches.
        // The new match count should differ from the initial one because one "foo" is gone.
        var lastCount = host.MatchCountCalls[^1];
        Assert.True(lastCount.Total < initialTotal,
            $"Expected total matches to drop after replace; was {initialTotal}, got {lastCount.Total}.");
    }
}
