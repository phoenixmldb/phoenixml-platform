using Avalonia.Controls;
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

        // XPath compile can be expensive — submit only on Enter. No debounce.
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
                    QuerySubmitted?.Invoke(queryBox.Text ?? string.Empty);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseRequested?.Invoke();
                e.Handled = true;
            }
        };

        prevButton.Click += (_, _) => PreviousRequested?.Invoke();
        nextButton.Click += (_, _) => QuerySubmitted?.Invoke(queryBox.Text ?? string.Empty);
        closeButton.Click += (_, _) => CloseRequested?.Invoke();
    }

#pragma warning disable CA1003 // Action<T> design is intentional per existing codebase conventions
    /// <summary>Raised on Enter inside the TextBox or click of the Next button.</summary>
    public event Action<string>? QuerySubmitted;

    /// <summary>Raised on Shift+Enter inside the TextBox or click of the Previous button.</summary>
    public event Action? PreviousRequested;

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
