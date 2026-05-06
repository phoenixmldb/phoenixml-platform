using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Phoenixml.Platform.Theming;

namespace Phoenixml.Editor;

/// <summary>Avalonia application definition for Phoenixml.Editor.</summary>
public sealed class App : Application
{
    /// <inheritdoc/>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted()
    {
        // Apply Phoenixml dark theme tokens to the application's resource dictionary
        // BEFORE any window opens. Without this, every {DynamicResource Phoenixml.Color.*}
        // lookup in Shell/Editing controls silently returns null and surfaces show the
        // FluentTheme default (which is also dark) — producing a uniform-dark void with no
        // visible pane separation. Applying populates the brush resources so layouts paint.
        FluentDarkTheme.Default.Apply(this);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var editor = new EditorMainWindow();
            desktop.MainWindow = editor.Window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
