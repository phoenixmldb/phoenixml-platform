using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Phoenixml.Platform.Shell;

/// <summary>
/// Application shell window. Provides custom title-bar chrome (per
/// <see cref="ChromePlatform"/>), a content slot apps inject their content into,
/// and a composable status bar.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ChromePlatform _chrome;

    /// <summary>Initializes a new <see cref="MainWindow"/> using the detected platform chrome.</summary>
    public MainWindow() : this(ChromePlatform.Detect()) { }

    /// <summary>Initializes a new <see cref="MainWindow"/> with the supplied <paramref name="chrome"/> configuration.</summary>
    public MainWindow(ChromePlatform chrome)
    {
        ArgumentNullException.ThrowIfNull(chrome);
        _chrome = chrome;
        InitializeComponent();
        ApplyChrome();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void ApplyChrome()
    {
        var windowsButtons = this.FindControl<StackPanel>("WindowsButtons");
        if (windowsButtons is not null)
            windowsButtons.IsVisible = _chrome.ShowWindowsChromeButtons;

        ExtendClientAreaToDecorationsHint = _chrome.ExtendsClientArea;
    }

    /// <summary>Sets the window's content (the central area between chrome and status bar).</summary>
    public void SetContent(object content)
    {
        var slot = this.FindControl<ContentPresenter>("ContentSlot");
        if (slot is not null) slot.Content = content;
    }

    /// <summary>Replaces the status bar segments.</summary>
    public void SetStatusSegments(IEnumerable<StatusBarSegment> segments, IServiceProvider services)
    {
        var bar = this.FindControl<StatusBar>("StatusBarControl");
        bar?.SetSegments(segments, services);
    }

    /// <summary>Updates the title bar text and the OS-level Title.</summary>
    public void SetTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        Title = title;
        var label = this.FindControl<TextBlock>("TitleLabel");
        if (label is not null) label.Text = title;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Maximize_Click(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
