using Avalonia.Controls;
using Avalonia.Layout;
using Phoenixml.Editor.Menus;
using Phoenixml.Editor.ViewModels;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editing;
using Phoenixml.Platform;
using Phoenixml.Platform.Shell;

namespace Phoenixml.Editor;

/// <summary>
/// Composes the editor window: wraps <see cref="MainWindow"/> from Shell and
/// injects the dual-pane (tree + text) layout into its content slot.
/// </summary>
/// <remarks>
/// <see cref="MainWindow"/> is sealed, so inheritance is not possible.
/// This class wraps it via composition and exposes the configured
/// <see cref="Window"/> instance via <see cref="Window"/>.
/// </remarks>
public sealed class EditorMainWindow
{
    private readonly MainWindow _window;
    private readonly EditorState _state = new();
    private readonly TreeEditorView _treeView;
    private readonly TextEditorView _textView;

    /// <summary>Initializes a new <see cref="EditorMainWindow"/>.</summary>
    public EditorMainWindow()
    {
        _window = new MainWindow();

        _treeView = new TreeEditorView();
        _textView = new TextEditorView { IsReadOnly = false };

        var split = new Platform.Shell.SplitView
        {
            Orientation = Orientation.Horizontal,
            Pane1Size = 360,
            Pane1Content = _treeView,
            Pane2Content = _textView,
        };

        _rootPanel = new DockPanel { LastChildFill = true };

        var menu = FileMenu.Build(this);
        DockPanel.SetDock(menu, Dock.Top);
        _rootPanel.Children.Add(menu);
        _rootPanel.Children.Add(split);

        _window.SetContent(_rootPanel);

        var docSegment = new StatusBarSegment(
            Id: "active-document",
            Label: "(no document)",
            Order: 0);
        _window.SetStatusSegments(new[] { docSegment }, EmptyServiceProvider.Instance);

        _window.SetTitle("Phoenixml Editor");

        _state.ActiveDocumentChanged += OnActiveDocumentChanged;
    }

    private readonly DockPanel _rootPanel;

    /// <summary>Gets the Avalonia <see cref="Avalonia.Controls.Window"/> to assign as the desktop main window.</summary>
    public Window Window => _window;

    /// <summary>Gets the editor view-model state (active document, etc.).</summary>
    internal EditorState State => _state;

    private void OnActiveDocumentChanged(XmlDocument? doc)
    {
        if (doc is null)
        {
            _treeView.SetProvider(null);
            _textView.Text = string.Empty;
            _window.SetTitle("Phoenixml Editor");
            return;
        }

        _treeView.SetProvider(doc.TreeNodes);
        _textView.Text = doc.SerializeToString();
        _window.SetTitle($"Phoenixml Editor — {doc.DisplayName}");
    }
}

/// <summary>
/// Stub <see cref="IServiceProvider"/> that returns <see langword="null"/> for every request.
/// Used for v0.1 status-bar segment wiring before real DI is plumbed in.
/// </summary>
internal sealed class EmptyServiceProvider : IServiceProvider
{
    /// <summary>The singleton instance.</summary>
    public static readonly EmptyServiceProvider Instance = new();

    private EmptyServiceProvider() { }

    /// <inheritdoc/>
    public object? GetService(Type serviceType) => null;
}
