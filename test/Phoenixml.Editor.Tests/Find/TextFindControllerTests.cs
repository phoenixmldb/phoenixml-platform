using Phoenixml.Editor.Find;
using Xunit;

namespace Phoenixml.Editor.Tests.Find;

public class TextFindControllerTests
{
    [Fact]
    public void EmptyQueryReturnsEmpty()
    {
        Assert.Empty(TextFindController.Find("hello", "", matchCase: false));
        Assert.Empty(TextFindController.Find("hello", null, matchCase: false));
    }

    [Fact]
    public void FindsMultipleOccurrences()
    {
        var matches = TextFindController.Find("abc abc abc", "abc", matchCase: false);
        Assert.Equal(3, matches.Count);
        Assert.Equal((0, 3), matches[0]);
        Assert.Equal((4, 3), matches[1]);
        Assert.Equal((8, 3), matches[2]);
    }

    [Fact]
    public void MatchCaseFiltersCorrectly()
    {
        var insensitive = TextFindController.Find("ABC abc Abc", "abc", matchCase: false);
        Assert.Equal(3, insensitive.Count);
        var sensitive = TextFindController.Find("ABC abc Abc", "abc", matchCase: true);
        Assert.Single(sensitive);
        Assert.Equal((4, 3), sensitive[0]);
    }

    [Fact]
    public void ReplaceAllReplacesEveryOccurrence()
    {
        Assert.Equal("XYZ XYZ XYZ", TextFindController.ReplaceAll("abc abc abc", "abc", "XYZ", matchCase: false));
        Assert.Equal("abc XYZ abc", TextFindController.ReplaceAll("abc ABC abc", "ABC", "XYZ", matchCase: true));
    }

    [Fact]
    public void ReplaceSingleReplacesAtOffset()
    {
        var result = TextFindController.ReplaceSingle("abc abc abc", offset: 4, length: 3, replacement: "XYZ");
        Assert.Equal("abc XYZ abc", result);
    }
}
