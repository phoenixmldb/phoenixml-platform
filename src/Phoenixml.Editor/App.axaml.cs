using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Phoenixml.Editor;

/// <summary>Avalonia application definition for Phoenixml.Editor.</summary>
public sealed class App : Application
{
    /// <inheritdoc/>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var editor = new EditorMainWindow();
            desktop.MainWindow = editor.Window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
