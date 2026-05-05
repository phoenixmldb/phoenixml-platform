using Avalonia.Controls;
using Phoenixml.Platform.Editing;
using Xunit;

namespace Phoenixml.Platform.Tests.Editing;

public class TextEditorViewTests
{
    [Fact]
    public void DefaultTextIsEmpty()
    {
        var view = new TextEditorView();
        Assert.Equal(string.Empty, view.Text);
    }

    [Fact]
    public void SetTextRoundTrips()
    {
        var view = new TextEditorView();
        view.Text = "hello world";
        Assert.Equal("hello world", view.Text);
    }

    [Fact]
    public void SetTextToEmptyRoundTrips()
    {
        var view = new TextEditorView();
        view.Text = "something";
        view.Text = string.Empty;
        Assert.Equal(string.Empty, view.Text);
    }

    [Fact]
    public void MultiLineTextRoundTrips()
    {
        var view = new TextEditorView();
        var multiline = "line one\nline two\nline three";
        view.Text = multiline;
        Assert.Equal(multiline, view.Text);
    }

    [Fact]
    public void IsReadOnlyDefaultIsFalse()
    {
        var view = new TextEditorView();
        Assert.False(view.IsReadOnly);
    }

    [Fact]
    public void IsReadOnlyRoundTrips()
    {
        var view = new TextEditorView();
        view.IsReadOnly = true;
        Assert.True(view.IsReadOnly);
        view.IsReadOnly = false;
        Assert.False(view.IsReadOnly);
    }

    [Fact]
    public void TextChangedEventIsSubscribable()
    {
        // Verify the event can be subscribed to without error.
        // Full event-fire testing requires a live Avalonia application (headless or real);
        // Avalonia.Headless.XUnit is not available for 11.2.x, deferred to manual validation.
        var view = new TextEditorView();
        view.TextChanged += _ => { };
        view.TextChanged -= _ => { };
    }

    [Fact]
    public void SetTextDoesNotFireTextChangedEvent()
    {
        // The public Text setter suppresses the event to avoid re-entrant loops.
        var view = new TextEditorView();
        var firedCount = 0;
        view.TextChanged += _ => firedCount++;
        view.Text = "programmatic";
        Assert.Equal(0, firedCount);
    }
}
