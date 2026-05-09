using System;
using PhoenixmlDb.Core;
using Phoenixml.Editor.Find;
using Xunit;

namespace Phoenixml.Editor.Tests.Find;

/// <summary>
/// Unit tests for <see cref="MatchIndex"/>.
/// </summary>
public class MatchIndexTests
{
    [Fact]
    public void ResetReplacesPriorState()
    {
        var index = new MatchIndex();
        var nodeA = new NodeId(1);
        var nodeB = new NodeId(2);

        // First reset: nodeA is a match.
        index.Reset([nodeA], active: nodeA);
        Assert.True(index.IsMatch(nodeA));
        Assert.True(index.IsActive(nodeA));

        // Second reset: only nodeB — nodeA must no longer appear.
        index.Reset([nodeB], active: nodeB);
        Assert.False(index.IsMatch(nodeA));
        Assert.False(index.IsActive(nodeA));
        Assert.True(index.IsMatch(nodeB));
        Assert.True(index.IsActive(nodeB));
    }

    [Fact]
    public void IsMatchTrueForKnownIds()
    {
        var index = new MatchIndex();
        var nodeA = new NodeId(10);
        var nodeB = new NodeId(20);
        var nodeC = new NodeId(30);

        index.Reset([nodeA, nodeB], active: null);

        Assert.True(index.IsMatch(nodeA));
        Assert.True(index.IsMatch(nodeB));
        Assert.False(index.IsMatch(nodeC));
    }

    [Fact]
    public void IsActiveTrueOnlyForActiveId()
    {
        var index = new MatchIndex();
        var nodeA = new NodeId(10);
        var nodeB = new NodeId(20);

        index.Reset([nodeA, nodeB], active: nodeA);

        Assert.True(index.IsActive(nodeA));
        Assert.False(index.IsActive(nodeB));
    }

    [Fact]
    public void EmptyMatchesAndNoActive()
    {
        var index = new MatchIndex();
        var nodeA = new NodeId(99);

        index.Reset([], active: null);

        Assert.False(index.IsMatch(nodeA));
        Assert.False(index.IsActive(nodeA));
        Assert.Empty(index.Matches);
        Assert.Null(index.Active);
    }
}
