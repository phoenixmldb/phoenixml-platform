using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Phoenixml.Platform.ChromeSpike;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // macOS uses native traffic-light buttons via ExtendClientAreaToDecorationsHint.
        // Win/Linux use the explicit min/max/close buttons we draw.
        var windowsButtons = this.FindControl<StackPanel>("WindowsButtons");
        if (windowsButtons is not null)
            windowsButtons.IsVisible = !RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        var platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows"
                     : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)     ? "macOS"
                     : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)   ? "Linux"
                     : "Unknown";

        var contentLabel = this.FindControl<TextBlock>("ContentLabel");
        if (contentLabel is not null) contentLabel.Text = $"Hello, {platform}";

        var statusLabel = this.FindControl<TextBlock>("StatusLabel");
        if (statusLabel is not null)
            statusLabel.Text = $"OS: {RuntimeInformation.OSDescription} | chrome spike";
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

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
