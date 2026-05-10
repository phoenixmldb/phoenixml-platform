using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Phoenixml.Editor.Find;

/// <summary>
/// Inline single-row find bar: TextBox + match-case toggle + match counter + prev/next/close
/// buttons. Embedded above the <c>NodeListView</c>; hidden when not in use.
/// </summary>
/// <remarks>
/// All interaction is event-based — the bar itself has no knowledge of the find service or
/// the document tree. <c>FindController</c> wires those together.
/// </remarks>
public sealed partial class FindBar : UserControl
{
    private CancellationTokenSource? _debounce;

    /// <summary>Initializes a new <see cref="FindBar"/>.</summary>
    public FindBar() => InitializeComponent();

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        var queryBox = this.FindControl<TextBox>("QueryBox")!;
        var matchCaseToggle = this.FindControl<ToggleButton>("MatchCaseToggle")!;
        var prevButton = this.FindControl<Button>("PrevButton")!;
        var nextButton = this.FindControl<Button>("NextButton")!;
        var closeButton = this.FindControl<Button>("CloseButton")!;
        var replaceToggle = this.FindControl<ToggleButton>("ReplaceToggle")!;
        var replaceRow = this.FindControl<StackPanel>("ReplaceRow")!;
        var replaceBox = this.FindControl<TextBox>("ReplaceBox")!;
        var replaceButton = this.FindControl<Button>("ReplaceButton")!;
        var replaceAllButton = this.FindControl<Button>("ReplaceAllButton")!;

        // Auto-focus when the bar is attached to the visual tree (called every time
        // IsVisible flips to true and the bar enters the tree).
        queryBox.AttachedToVisualTree += (_, _) =>
        {
            Dispatcher.UIThread.Post(() => queryBox.Focus(), DispatcherPriority.Background);
        };

        // Debounced text change.
        queryBox.TextChanged += (_, _) => ScheduleQueryChanged();

        // Key handling inside the TextBox.
        queryBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    PreviousRequested?.Invoke();
                }
                else
                {
                    NextRequested?.Invoke();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseRequested?.Invoke();
                e.Handled = true;
            }
        };

        // Match-case toggle fires immediately (intentional, no debounce).
        matchCaseToggle.IsCheckedChanged += (_, _) =>
        {
            var query = queryBox.Text ?? string.Empty;
            var matchCase = matchCaseToggle.IsChecked == true;
            QueryChanged?.Invoke(query, matchCase);
        };

        prevButton.Click += (_, _) => PreviousRequested?.Invoke();
        nextButton.Click += (_, _) => NextRequested?.Invoke();
        closeButton.Click += (_, _) => CloseRequested?.Invoke();

        replaceToggle.IsCheckedChanged += (_, _) =>
        {
            var show = replaceToggle.IsChecked == true;
            replaceRow.IsVisible = show;
            replaceToggle.Content = show ? "▾" : "▸";
        };

        replaceButton.Click += (_, _) => ReplaceCurrentRequested?.Invoke(replaceBox.Text ?? string.Empty);
        replaceAllButton.Click += (_, _) => ReplaceAllRequested?.Invoke(replaceBox.Text ?? string.Empty);
    }

    // -------------------------------------------------------------------------
    // Public surface
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raised when the query text or match-case toggle changes (debounced 200ms for text;
    /// immediate for toggle). Arguments are (query, matchCase).
    /// </summary>
#pragma warning disable CA1003 // Action<T> design is intentional per existing codebase conventions
    public event Action<string, bool>? QueryChanged;

    /// <summary>Raised on Enter inside the TextBox or click of the Next button.</summary>
    public event Action? NextRequested;

    /// <summary>Raised on Shift+Enter inside the TextBox or click of the Previous button.</summary>
    public event Action? PreviousRequested;

    /// <summary>Raised on Escape inside the TextBox or click of the Close button.</summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Raised when the user clicks the Replace button. Argument is the replacement string
    /// (may be empty). Applies to the currently active match only.
    /// </summary>
    /// <remarks>
    /// Deliberately not triggered by Enter on the Replace TextBox to avoid accidental
    /// destructive-action mis-fires.
    /// </remarks>
    public event Action<string>? ReplaceCurrentRequested;

    /// <summary>
    /// Raised when the user clicks the Replace All button. Argument is the replacement string
    /// (may be empty). Applies to all current matches.
    /// </summary>
    public event Action<string>? ReplaceAllRequested;
#pragma warning restore CA1003

    /// <summary>
    /// Updates the match counter display.
    /// Shows empty string when both are zero and no query is active;
    /// "no matches" when the query is non-empty but total is zero;
    /// otherwise "{active} / {total}".
    /// </summary>
    /// <param name="active">The 1-based index of the currently active match (pass 0 for none).</param>
    /// <param name="total">The total number of matches.</param>
    public void SetMatchCount(int active, int total)
    {
        var counter = this.FindControl<TextBlock>("CounterText")!;
        var queryBox = this.FindControl<TextBox>("QueryBox")!;
        var hasQuery = !string.IsNullOrEmpty(queryBox.Text);

        counter.Text = (active, total, hasQuery) switch
        {
            (_, 0, false) => string.Empty,
            (_, 0, true)  => "no matches",
            _             => $"{active} / {total}",
        };
    }

    /// <summary>
    /// Focuses the query TextBox. When the bar is already open, also selects all text so
    /// the user can type a new query or press Enter to cycle without clearing first.
    /// </summary>
    public void FocusQueryBox()
    {
        var queryBox = this.FindControl<TextBox>("QueryBox")!;
        Dispatcher.UIThread.Post(() =>
        {
            queryBox.Focus();
            queryBox.SelectAll();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Expands the Replace row and focuses the Replace TextBox.
    /// If the row is already visible, focus is re-applied so the user can type
    /// the replacement string without any further interaction.
    /// </summary>
    public void OpenReplaceMode()
    {
        var replaceToggle = this.FindControl<ToggleButton>("ReplaceToggle")!;
        replaceToggle.IsChecked = true;  // triggers IsCheckedChanged → row visible + chevron update
        var replaceBox = this.FindControl<TextBox>("ReplaceBox")!;
        Dispatcher.UIThread.Post(() => replaceBox.Focus(), DispatcherPriority.Background);
    }

    /// <summary>
    /// Resets the bar to empty state: clears both the Find and Replace TextBox content
    /// and the counter. Does not fire <see cref="QueryChanged"/>.
    /// The Replace row toggle state is preserved — if the user had it open it stays open.
    /// </summary>
    public void Reset()
    {
        _debounce?.Cancel();
        _debounce = null;

        var queryBox = this.FindControl<TextBox>("QueryBox")!;
        var counter = this.FindControl<TextBlock>("CounterText")!;
        var replaceBox = this.FindControl<TextBox>("ReplaceBox")!;
        // Suppress the TextChanged → debounce → QueryChanged path while resetting.
        queryBox.TextChanged -= OnTextChangedForDebounce;
        queryBox.Text = string.Empty;
        queryBox.TextChanged += OnTextChangedForDebounce;
        counter.Text = string.Empty;
        replaceBox.Text = string.Empty;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void ScheduleQueryChanged()
    {
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        var ct = _debounce.Token;
        _ = RunDebouncedAsync(ct);
    }

    private async Task RunDebouncedAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(200, ct);
            ct.ThrowIfCancellationRequested();

            var queryBox = this.FindControl<TextBox>("QueryBox")!;
            var matchCaseToggle = this.FindControl<ToggleButton>("MatchCaseToggle")!;
            var query = queryBox.Text ?? string.Empty;
            var matchCase = matchCaseToggle.IsChecked == true;
            QueryChanged?.Invoke(query, matchCase);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke — discard silently.
        }
    }

    // Stub handler kept for the Reset symmetry. Not actually subscribed — Reset uses
    // the remove/add pattern with the real lambda captured in InitializeComponent.
    private void OnTextChangedForDebounce(object? s, TextChangedEventArgs e) =>
        ScheduleQueryChanged();
}
