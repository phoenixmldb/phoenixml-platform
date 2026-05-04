using Phoenixml.Platform.Abstractions.Editing;
using Xunit;

namespace Phoenixml.Platform.Tests.Editing;

public class FindOptionsTests
{
    [Fact]
    public void DefaultSearchTextIsEmpty()
    {
        var opts = new FindOptions();
        Assert.Equal(string.Empty, opts.SearchText);
    }

    [Fact]
    public void DefaultReplaceTextIsEmpty()
    {
        var opts = new FindOptions();
        Assert.Equal(string.Empty, opts.ReplaceText);
    }

    [Fact]
    public void DefaultMatchCaseIsFalse()
    {
        var opts = new FindOptions();
        Assert.False(opts.MatchCase);
    }

    [Fact]
    public void DefaultMatchWholeWordIsFalse()
    {
        var opts = new FindOptions();
        Assert.False(opts.MatchWholeWord);
    }

    [Fact]
    public void DefaultUseRegexIsFalse()
    {
        var opts = new FindOptions();
        Assert.False(opts.UseRegex);
    }

    [Fact]
    public void DefaultUseXPathIsFalse()
    {
        var opts = new FindOptions();
        Assert.False(opts.UseXPath);
    }

    [Fact]
    public void DefaultScopeIsCurrentDocument()
    {
        var opts = new FindOptions();
        Assert.Equal(FindScope.CurrentDocument, opts.Scope);
    }

    [Fact]
    public void WithExpressionPreservesUnchangedFields()
    {
        var original = new FindOptions
        {
            SearchText = "foo",
            ReplaceText = "bar",
            MatchCase = true,
            MatchWholeWord = true,
            UseRegex = false,
            UseXPath = false,
            Scope = FindScope.AllOpenDocuments
        };

        var modified = original with { SearchText = "baz" };

        Assert.Equal("baz", modified.SearchText);
        Assert.Equal("bar", modified.ReplaceText);
        Assert.True(modified.MatchCase);
        Assert.True(modified.MatchWholeWord);
        Assert.False(modified.UseRegex);
        Assert.False(modified.UseXPath);
        Assert.Equal(FindScope.AllOpenDocuments, modified.Scope);
    }

    [Fact]
    public void WithExpressionDoesNotMutateOriginal()
    {
        var original = new FindOptions { SearchText = "original" };
        var _ = original with { SearchText = "modified" };
        Assert.Equal("original", original.SearchText);
    }

    [Fact]
    public void RecordEqualityHoldsForIdenticalValues()
    {
        var a = new FindOptions { SearchText = "x", MatchCase = true };
        var b = new FindOptions { SearchText = "x", MatchCase = true };
        Assert.Equal(a, b);
    }
}
