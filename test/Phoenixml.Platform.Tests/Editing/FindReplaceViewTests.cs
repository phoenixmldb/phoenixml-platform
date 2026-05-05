using Phoenixml.Platform.Abstractions.Editing;
using Phoenixml.Platform.Editing;
using Xunit;

namespace Phoenixml.Platform.Tests.Editing;

/// <summary>
/// Tests for <see cref="FindReplaceViewModel"/> (internal record, tested via InternalsVisibleTo).
/// UI-wired event tests require a live Avalonia app; deferred to manual validation since
/// Avalonia.Headless.XUnit is not available for 11.2.x.
/// </summary>
public class FindReplaceViewTests
{
    // ---- FindReplaceViewModel tests (pure logic) ----

    [Fact]
    public void ViewModelDefaultsMatchFindOptionsDefaults()
    {
        var vm = new FindReplaceViewModel();
        var opts = vm.Build();
        Assert.Equal(string.Empty, opts.SearchText);
        Assert.Equal(string.Empty, opts.ReplaceText);
        Assert.False(opts.MatchCase);
        Assert.False(opts.MatchWholeWord);
        Assert.False(opts.UseRegex);
        Assert.False(opts.UseXPath);
        Assert.Equal(FindScope.CurrentDocument, opts.Scope);
    }

    [Fact]
    public void ViewModelBuildReflectsSearchText()
    {
        var vm = new FindReplaceViewModel { SearchText = "foo" };
        Assert.Equal("foo", vm.Build().SearchText);
    }

    [Fact]
    public void ViewModelBuildReflectsReplaceText()
    {
        var vm = new FindReplaceViewModel { ReplaceText = "bar" };
        Assert.Equal("bar", vm.Build().ReplaceText);
    }

    [Fact]
    public void ViewModelBuildReflectsMatchCase()
    {
        var vm = new FindReplaceViewModel { MatchCase = true };
        Assert.True(vm.Build().MatchCase);
    }

    [Fact]
    public void ViewModelBuildReflectsMatchWholeWord()
    {
        var vm = new FindReplaceViewModel { MatchWholeWord = true };
        Assert.True(vm.Build().MatchWholeWord);
    }

    [Fact]
    public void ViewModelBuildReflectsUseRegex()
    {
        var vm = new FindReplaceViewModel { UseRegex = true };
        Assert.True(vm.Build().UseRegex);
    }

    [Fact]
    public void ViewModelBuildReflectsUseXPath()
    {
        var vm = new FindReplaceViewModel { UseXPath = true };
        Assert.True(vm.Build().UseXPath);
    }

    [Fact]
    public void ViewModelBuildReflectsScope()
    {
        var vm = new FindReplaceViewModel { Scope = FindScope.ConnectedCollection };
        Assert.Equal(FindScope.ConnectedCollection, vm.Build().Scope);
    }

    [Fact]
    public void ViewModelWithExpressionPreservesOtherFields()
    {
        var vm = new FindReplaceViewModel
        {
            SearchText = "original",
            MatchCase = true,
            Scope = FindScope.AllOpenDocuments
        };
        var updated = vm with { SearchText = "updated" };

        Assert.Equal("updated", updated.SearchText);
        Assert.True(updated.MatchCase);
        Assert.Equal(FindScope.AllOpenDocuments, updated.Scope);
    }

    // ---- FindReplaceView smoke tests ----

    [Fact]
    public void ViewDefaultOptionsMatchDefaults()
    {
        var view = new FindReplaceView();
        var opts = view.Options;
        Assert.Equal(string.Empty, opts.SearchText);
        Assert.Equal(FindScope.CurrentDocument, opts.Scope);
    }

    [Fact]
    public void ViewEventsAreSubscribable()
    {
        var view = new FindReplaceView();
        view.FindRequested += _ => { };
        view.FindNextRequested += _ => { };
        view.ReplaceRequested += _ => { };
        view.ReplaceAllRequested += _ => { };
    }
}
