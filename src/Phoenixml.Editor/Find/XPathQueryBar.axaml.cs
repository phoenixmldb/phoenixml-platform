using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace Phoenixml.Editor.Find;

/// <summary>
/// Inline single-row XPath query bar: input TextBox + match counter + prev/next/close buttons.
/// Embedded above the <c>NodeListView</c>; hidden when not in use. The bar is dumb — it owns no
/// engine or match state. <c>XPathQueryController</c> wires the events to the service.
/// </summary>
public sealed partial class XPathQueryBar : UserControl
{
    private static readonly IBrush NormalBorder = new SolidColorBrush(Color.Parse("#4A4A4F"));
    private static readonly IBrush ErrorBorder = new SolidColorBrush(Color.Parse("#D16969"));

    /// <summary>Initializes a new <see cref="XPathQueryBar"/>.</summary>
    public XPathQueryBar() => InitializeComponent();

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        var queryBox = this.FindControl<TextBox>("QueryBox")!;
        var prevButton = this.FindControl<Button>("PrevButton")!;
        var nextButton = this.FindControl<Button>("NextButton")!;
        var closeButton = this.FindControl<Button>("CloseButton")!;

        queryBox.AttachedToVisualTree += (_, _) =>
        {
            Dispatcher.UIThread.Post(() => queryBox.Focus(), DispatcherPriority.Background);
        };

        var popup = this.FindControl<Popup>("CompletionPopup")!;
        var completionList = this.FindControl<ListBox>("CompletionList")!;
        var signaturePopup = this.FindControl<Popup>("SignaturePopup")!;
        var signatureText = this.FindControl<TextBlock>("SignatureText")!;

        queryBox.TextChanged += (_, _) =>
        {
            var fnName = FunctionNameBeforeOpenParen(queryBox.Text, queryBox.CaretIndex);
            if (fnName is not null && XPathSignatures.Get(fnName) is string sig)
            {
                signatureText.Text = sig;
                signaturePopup.IsOpen = true;
            }
            else
            {
                signaturePopup.IsOpen = false;
            }
        };

        // Open / refresh the popup as the user types. The context provider classifies
        // the caret (function name / element name / attribute name) so the completion
        // source can switch between built-in fn:* names and host-harvested element /
        // attribute names from the active document.
        queryBox.TextChanged += (_, _) =>
        {
            var ctx = XPathContextProvider.DetectContext(queryBox.Text, queryBox.CaretIndex);
            if (ctx.Kind == XPathContextKind.None)
            {
                popup.IsOpen = false;
                return;
            }

            IReadOnlyList<string> matches = ctx.Kind switch
            {
                XPathContextKind.FunctionName => XPathFunctionList.Match(ctx.Prefix),
                XPathContextKind.ElementName or XPathContextKind.AttributeName =>
                    NamesRequested?.Invoke(ctx.Kind, ctx.Prefix) ?? Array.Empty<string>(),
                _ => Array.Empty<string>(),
            };

            if (matches.Count == 0)
            {
                popup.IsOpen = false;
                return;
            }
            completionList.ItemsSource = matches;
            completionList.SelectedIndex = 0;
            popup.IsOpen = true;
        };

        // XPath compile can be expensive — submit only on Enter. No debounce.
        // Up/Down cycle through history (most-recent first when going Up).
        // When the completion popup is open, Up/Down navigate the popup instead.
        int historyCursor = -1;
        queryBox.KeyDown += (_, e) =>
        {
            // Popup-priority handling: Tab accepts, Esc closes, Up/Down navigate.
            if (popup.IsOpen)
            {
                if (e.Key == Key.Tab)
                {
                    if (completionList.SelectedItem is string pick)
                        ReplacePrefixAtCaret(queryBox, pick);
                    popup.IsOpen = false;
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Escape)
                {
                    popup.IsOpen = false;
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Up)
                {
                    completionList.SelectedIndex = Math.Max(0, completionList.SelectedIndex - 1);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Down)
                {
                    var count = (completionList.ItemsSource as System.Collections.ICollection)?.Count ?? 0;
                    completionList.SelectedIndex = Math.Min(count - 1, completionList.SelectedIndex + 1);
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Enter)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    PreviousRequested?.Invoke();
                }
                else
                {
                    QuerySubmitted?.Invoke(queryBox.Text ?? string.Empty);
                    historyCursor = -1;
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseRequested?.Invoke();
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                var hist = HistoryRequested?.Invoke();
                if (hist is null || hist.Count == 0) return;
                historyCursor = Math.Min(historyCursor + 1, hist.Count - 1);
                queryBox.Text = hist[historyCursor];
                queryBox.CaretIndex = queryBox.Text.Length;
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                var hist = HistoryRequested?.Invoke();
                if (hist is null || hist.Count == 0) return;
                historyCursor = Math.Max(historyCursor - 1, -1);
                queryBox.Text = historyCursor == -1 ? string.Empty : hist[historyCursor];
                queryBox.CaretIndex = queryBox.Text.Length;
                e.Handled = true;
            }
        };

        prevButton.Click += (_, _) => PreviousRequested?.Invoke();
        nextButton.Click += (_, _) => QuerySubmitted?.Invoke(queryBox.Text ?? string.Empty);
        closeButton.Click += (_, _) => CloseRequested?.Invoke();
    }

    private static string? WordPrefixAtCaret(string? text, int caret)
    {
        if (string.IsNullOrEmpty(text) || caret <= 0) return null;
        int start = caret;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        if (start == caret) return null;
        return text.Substring(start, caret - start);
    }

    private static void ReplacePrefixAtCaret(TextBox tb, string replacement)
    {
        var text = tb.Text ?? string.Empty;
        var caret = tb.CaretIndex;
        int start = caret;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        var newText = string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(caret));
        tb.Text = newText;
        tb.CaretIndex = start + replacement.Length;
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '-' || c == '_';

    /// <summary>
    /// Finds the function name immediately before the nearest unmatched open paren to
    /// the left of the caret. Returns null if none. Used to trigger the signature popup.
    /// </summary>
    private static string? FunctionNameBeforeOpenParen(string? text, int caret)
    {
        if (string.IsNullOrEmpty(text) || caret == 0) return null;
        int depth = 0;
        int parenIndex = -1;
        for (int i = caret - 1; i >= 0; i--)
        {
            var c = text[i];
            if (c == ')') depth++;
            else if (c == '(')
            {
                if (depth == 0) { parenIndex = i; break; }
                depth--;
            }
        }
        if (parenIndex <= 0) return null;
        int start = parenIndex;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        if (start == parenIndex) return null;
        return text.Substring(start, parenIndex - start);
    }

#pragma warning disable CA1003 // Action<T> design is intentional per existing codebase conventions
    /// <summary>Raised on Enter inside the TextBox or click of the Next button.</summary>
    public event Action<string>? QuerySubmitted;

    /// <summary>Raised on Shift+Enter inside the TextBox or click of the Previous button.</summary>
    public event Action? PreviousRequested;

    /// <summary>
    /// Requests a snapshot of submitted-XPath history from the host. Returns the list
    /// most-recent first. Called when Up/Down cycles through history.
    /// </summary>
    public event Func<IReadOnlyList<string>>? HistoryRequested;

    /// <summary>
    /// Requests element or attribute name completions from the host (typically harvested
    /// from the active document). The host receives the context kind and the prefix the
    /// user has typed, and returns a list of completion candidates.
    /// </summary>
    public event Func<XPathContextKind, string, IReadOnlyList<string>>? NamesRequested;

    /// <summary>Raised on Escape inside the TextBox or click of the Close button.</summary>
    public event Action? CloseRequested;
#pragma warning restore CA1003

    /// <summary>
    /// Updates the match counter display. When <paramref name="error"/> is non-null, the
    /// counter shows the error and the input border turns red.
    /// </summary>
    public void SetMatchCount(int active, int total, string? error = null)
    {
        var counter = this.FindControl<TextBlock>("CounterText")!;
        var queryBox = this.FindControl<TextBox>("QueryBox")!;

        if (error is not null)
        {
            counter.Text = error;
            counter.Foreground = ErrorBorder;
            queryBox.BorderBrush = ErrorBorder;
            return;
        }

        counter.Foreground = new SolidColorBrush(Color.Parse("#9D9D9D"));
        queryBox.BorderBrush = NormalBorder;
        counter.Text = (active, total) switch
        {
            (_, 0) => "no matches",
            _ => $"{active} / {total}",
        };
    }

    /// <summary>Focuses the query TextBox and selects all text.</summary>
    public void FocusQueryBox()
    {
        var queryBox = this.FindControl<TextBox>("QueryBox")!;
        Dispatcher.UIThread.Post(() =>
        {
            queryBox.Focus();
            queryBox.SelectAll();
        }, DispatcherPriority.Background);
    }

    /// <summary>Clears the input and counter, restoring the bar to its initial state.</summary>
    public void Reset()
    {
        var queryBox = this.FindControl<TextBox>("QueryBox")!;
        var counter = this.FindControl<TextBlock>("CounterText")!;
        queryBox.Text = string.Empty;
        queryBox.BorderBrush = NormalBorder;
        counter.Text = string.Empty;
        counter.Foreground = new SolidColorBrush(Color.Parse("#9D9D9D"));
    }
}
