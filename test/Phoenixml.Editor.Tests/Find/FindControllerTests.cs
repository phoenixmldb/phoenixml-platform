using System.Collections.Generic;
using PhoenixmlDb.Core;
using Phoenixml.Workbench.Find;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Tree;
using Xunit;
using PlatformDocumentId = Phoenixml.Platform.Abstractions.Documents.DocumentId;

namespace Phoenixml.Editor.Tests.Find;

/// <summary>
/// Unit tests for <see cref="FindController"/>. Uses a stub <see cref="IFindHost"/>
/// to assert on navigation and counter calls without requiring a running Avalonia app.
/// </summary>
public class FindControllerTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Minimal fixture XML containing two &lt;book&gt; elements.</summary>
    private const string LibraryXml = """
        <library>
          <book isbn="978-0-13-110362-7">
            <title>The C Programming Language</title>
            <author>Kernighan</author>
          </book>
          <book isbn="978-0-13-468599-1">
            <title>Effective Java</title>
            <author>Bloch</author>
          </book>
        </library>
        """;

    private static XmlDocument MakeDoc(string xml)
    {
        var (result, nsTable) = XmlDocument.ParseXml(xml);
        return new XmlDocument(PlatformDocumentId.New(), source: null, "test.xml", result, nsTable);
    }

    // -------------------------------------------------------------------------
    // Stub IFindHost
    // -------------------------------------------------------------------------

    /// <summary>
    /// Test double for <see cref="IFindHost"/>. Records calls so tests can assert
    /// on navigation, counter updates, and hide invocations.
    /// </summary>
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
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void OnQueryChangedSetsActiveToFirstMatch()
    {
        var doc = MakeDoc(LibraryXml);
        var (controller, host, index) = Make(doc);

        controller.OnQueryChanged("book", matchCase: false);

        // Two <book> elements — active should be the first (index 0).
        Assert.Equal(2, index.Matches.Count);
        Assert.NotNull(index.Active);
        // active is 1-based in the counter, 1/2 expected.
        Assert.Single(host.MatchCountCalls);
        Assert.Equal((1, 2), host.MatchCountCalls[0]);
        Assert.NotNull(host.LastSelectedNodeId);
    }

    [Fact]
    public void OnNextCyclesForward()
    {
        var doc = MakeDoc(LibraryXml);
        var (controller, host, _) = Make(doc);

        controller.OnQueryChanged("book", matchCase: false);
        var firstId = host.LastSelectedNodeId;

        controller.OnNext();
        var secondId = host.LastSelectedNodeId;

        Assert.NotEqual(firstId, secondId);
        Assert.Equal(2, host.MatchCountCalls.Count);
        Assert.Equal((2, 2), host.MatchCountCalls[1]);
    }

    [Fact]
    public void OnNextWrapsAroundFromLast()
    {
        var doc = MakeDoc(LibraryXml);
        var (controller, host, _) = Make(doc);

        controller.OnQueryChanged("book", matchCase: false);
        var firstId = host.LastSelectedNodeId;

        // Advance to match index 1 (last), then wrap to 0.
        controller.OnNext();
        controller.OnNext();

        Assert.Equal(firstId, host.LastSelectedNodeId);
        Assert.Equal((1, 2), host.MatchCountCalls[^1]);
    }

    [Fact]
    public void OnPreviousCyclesBackward()
    {
        var doc = MakeDoc(LibraryXml);
        var (controller, host, _) = Make(doc);

        controller.OnQueryChanged("book", matchCase: false);
        controller.OnNext();
        var secondId = host.LastSelectedNodeId;

        controller.OnPrevious();
        var backToFirstId = host.LastSelectedNodeId;

        Assert.NotEqual(secondId, backToFirstId);
        Assert.Equal((1, 2), host.MatchCountCalls[^1]);
    }

    [Fact]
    public void OnPreviousWrapsAroundFromFirst()
    {
        var doc = MakeDoc(LibraryXml);
        var (controller, host, _) = Make(doc);

        controller.OnQueryChanged("book", matchCase: false);
        var firstId = host.LastSelectedNodeId;

        // Previous from first should wrap to last.
        controller.OnPrevious();

        Assert.NotEqual(firstId, host.LastSelectedNodeId);
        Assert.Equal((2, 2), host.MatchCountCalls[^1]);
    }

    [Fact]
    public void OnCloseClearsState()
    {
        var doc = MakeDoc(LibraryXml);
        var (controller, host, index) = Make(doc);

        controller.OnQueryChanged("book", matchCase: false);
        Assert.NotEmpty(index.Matches);

        controller.OnClose();

        Assert.Empty(index.Matches);
        Assert.Null(index.Active);
        Assert.Equal(1, host.HideFindBarCallCount);
    }

    [Fact]
    public void OnQueryChangedNoMatchesActiveIsNegativeOne()
    {
        var doc = MakeDoc(LibraryXml);
        var (controller, host, index) = Make(doc);

        controller.OnQueryChanged("zzznomatch", matchCase: false);

        Assert.Empty(index.Matches);
        Assert.Null(index.Active);
        Assert.Single(host.MatchCountCalls);
        Assert.Equal((0, 0), host.MatchCountCalls[0]);
        // No navigation call expected — nothing to select.
        Assert.Null(host.LastSelectedNodeId);
    }
}
