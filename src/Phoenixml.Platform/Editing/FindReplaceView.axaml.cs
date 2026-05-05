using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Phoenixml.Platform.Abstractions.Editing;

namespace Phoenixml.Platform.Editing;

/// <summary>
/// UI panel for find/replace. Exposes <see cref="Options"/> as the current settings snapshot;
/// raises events when the user clicks action buttons. The host performs the actual search.
/// </summary>
public sealed partial class FindReplaceView : UserControl
{
    /// <summary>Initializes a new instance of <see cref="FindReplaceView"/>.</summary>
    public FindReplaceView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private FindReplaceViewModel _vm = new();

    /// <summary>Gets the current find/replace options, rebuilt on every UI change.</summary>
    public FindOptions Options => _vm.Build();

    /// <summary>Raised when the user clicks the Find button.</summary>
#pragma warning disable CA1003 // Use generic event handler instances — intentional; Action<T> pattern
    public event Action<FindOptions>? FindRequested;

    /// <summary>Raised when the user clicks the Find Next button.</summary>
    public event Action<FindOptions>? FindNextRequested;

    /// <summary>Raised when the user clicks the Replace button.</summary>
    public event Action<FindOptions>? ReplaceRequested;

    /// <summary>Raised when the user clicks the Replace All button.</summary>
    public event Action<FindOptions>? ReplaceAllRequested;
#pragma warning restore CA1003

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) _vm = _vm with { SearchText = tb.Text ?? string.Empty };
    }

    private void ReplaceBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) _vm = _vm with { ReplaceText = tb.Text ?? string.Empty };
    }

    private void Option_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var mc = this.FindControl<CheckBox>("MatchCaseBox")?.IsChecked == true;
        var ww = this.FindControl<CheckBox>("WholeWordBox")?.IsChecked == true;
        var rx = this.FindControl<CheckBox>("RegexBox")?.IsChecked == true;
        var xp = this.FindControl<CheckBox>("XPathBox")?.IsChecked == true;
        _vm = _vm with { MatchCase = mc, MatchWholeWord = ww, UseRegex = rx, UseXPath = xp };
    }

    private void ScopeBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb)
        {
            var scope = cb.SelectedIndex switch
            {
                1 => FindScope.AllOpenDocuments,
                2 => FindScope.ConnectedCollection,
                _ => FindScope.CurrentDocument
            };
            _vm = _vm with { Scope = scope };
        }
    }

    private void Find_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => FindRequested?.Invoke(Options);

    private void FindNext_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => FindNextRequested?.Invoke(Options);

    private void Replace_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ReplaceRequested?.Invoke(Options);

    private void ReplaceAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ReplaceAllRequested?.Invoke(Options);
}

/// <summary>
/// Mutable state backing for <see cref="FindReplaceView"/>. Modelled as a record so that
/// each UI-driven change produces a fresh snapshot; <see cref="Build"/> creates a
/// <see cref="FindOptions"/> record from the current state.
/// </summary>
internal sealed record FindReplaceViewModel
{
    public string SearchText { get; init; } = string.Empty;
    public string ReplaceText { get; init; } = string.Empty;
    public bool MatchCase { get; init; }
    public bool MatchWholeWord { get; init; }
    public bool UseRegex { get; init; }
    public bool UseXPath { get; init; }
    public FindScope Scope { get; init; } = FindScope.CurrentDocument;

    /// <summary>Builds a <see cref="FindOptions"/> snapshot from the current state.</summary>
    public FindOptions Build() => new()
    {
        SearchText = SearchText,
        ReplaceText = ReplaceText,
        MatchCase = MatchCase,
        MatchWholeWord = MatchWholeWord,
        UseRegex = UseRegex,
        UseXPath = UseXPath,
        Scope = Scope
    };
}
