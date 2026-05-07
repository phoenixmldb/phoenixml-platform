using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Phoenixml.Editor.Commands;
using Phoenixml.Editor.Menus;
using Phoenixml.Editor.ViewModels;
using Phoenixml.Editor.Views;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Tree;
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
    private readonly PropertiesPane _propertiesPane;
    private Phoenixml.Platform.Abstractions.Documents.IDocument? _subscribedDocument;

    /// <summary>Initializes a new <see cref="EditorMainWindow"/>.</summary>
    public EditorMainWindow()
    {
        _window = new MainWindow();

        _treeView = new TreeEditorView();
        _textView = new TextEditorView { IsReadOnly = false };
        _propertiesPane = new PropertiesPane();

        // Left pane: tree (star) + separator (1 dip) + properties pane (140 dip).
        var leftPaneGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,140"),
        };

        var separator = new Border
        {
            Height = 1,
            Background = Application.Current?.TryFindResource("Phoenixml.Color.Border", out var br) == true
                ? br as IBrush
                : null,
        };

        Grid.SetRow(_treeView, 0);
        Grid.SetRow(separator, 1);
        Grid.SetRow(_propertiesPane, 2);

        leftPaneGrid.Children.Add(_treeView);
        leftPaneGrid.Children.Add(separator);
        leftPaneGrid.Children.Add(_propertiesPane);

        var split = new Platform.Shell.SplitView
        {
            Orientation = Orientation.Horizontal,
            Pane1Size = 360,
            Pane1Content = leftPaneGrid,
            Pane2Content = _textView,
        };

        // Wire tree selection to the properties pane.
        _treeView.SelectionChanged += node =>
        {
            var doc = _state.ActiveDocument;
            _propertiesPane.OnSelectionChanged(doc, node as XmlTreeNode);
        };

        _rootPanel = new DockPanel { LastChildFill = true };

        var menu = new Menu
        {
            ItemsSource = new[] { FileMenu.Build(this), EditMenu.Build(this) },
        };
        DockPanel.SetDock(menu, Dock.Top);
        _rootPanel.Children.Add(menu);
        _rootPanel.Children.Add(split);

        _window.SetContent(_rootPanel);

        // Window-wide keyboard shortcuts — fire regardless of which control has focus.
        _window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Z, KeyModifiers.Control),
            Command = new RelayCommand(() => UndoCommand.Execute(this)),
        });
        _window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Y, KeyModifiers.Control),
            Command = new RelayCommand(() => RedoCommand.Execute(this)),
        });
        _window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.O, KeyModifiers.Control),
            Command = new RelayCommand(async () =>
                await OpenFileCommand.ExecuteAsync(this, CancellationToken.None)),
        });
        _window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.S, KeyModifiers.Control),
            Command = new RelayCommand(async () =>
                await SaveFileCommand.ExecuteAsync(this, CancellationToken.None)),
        });

        UpdateStatusBar();

        _window.SetTitle("Phoenixml Editor");

        _state.ActiveDocumentChanged += OnActiveDocumentChanged;
    }

    private void UpdateStatusBar()
    {
        var doc = _state.ActiveDocument;
        var label = doc is null ? "(no document)" : $"{doc.DisplayName}";
        var segment = new StatusBarSegment(
            Id: "active-document",
            Label: label,
            Order: 0);
        _window.SetStatusSegments(new[] { segment }, EmptyServiceProvider.Instance);
    }

    private readonly DockPanel _rootPanel;

    /// <summary>Gets the Avalonia <see cref="Avalonia.Controls.Window"/> to assign as the desktop main window.</summary>
    public Window Window => _window;

    /// <summary>Gets the editor view-model state (active document, etc.).</summary>
    internal EditorState State => _state;

    private void OnActiveDocumentChanged(XmlDocument? doc)
    {
        // Unsubscribe from any previously active document.
        if (_subscribedDocument is not null)
            _subscribedDocument.Changed -= OnSubscribedDocumentChanged;
        _subscribedDocument = doc;

        if (doc is null)
        {
            _treeView.SetProvider(null);
            _textView.Text = string.Empty;
            _propertiesPane.OnSelectionChanged(null, null);
            _window.SetTitle("Phoenixml Editor");
            UpdateStatusBar();
            return;
        }

        _treeView.SetProvider(doc.TreeNodes);
        _textView.Text = doc.SerializeToString();

        _window.SetTitle($"Phoenixml Editor — {doc.DisplayName}");
        UpdateStatusBar();

        doc.Changed += OnSubscribedDocumentChanged;
    }

    private void OnSubscribedDocumentChanged(Phoenixml.Platform.Abstractions.Documents.DocumentChange change)
    {
        // Rebuild the text view's serialized output after each edit. The tree view refreshes
        // via XmlTreeNodeProvider.Changed (it subscribes to the same Document.Changed).
        if (_state.ActiveDocument is XmlDocument doc)
        {
            _textView.Text = doc.SerializeToString();
            UpdateStatusBar();
        }
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
