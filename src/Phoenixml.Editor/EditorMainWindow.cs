using Avalonia.Controls;
using Avalonia.Input;
using Phoenixml.Editor.Commands;
using Phoenixml.Editor.Menus;
using Phoenixml.Editor.ViewModels;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Editing;
using Phoenixml.Platform.Editor.Xml.Tree;
using Phoenixml.Platform.Editing;
using Phoenixml.Platform;
using Phoenixml.Platform.Shell;
using PhoenixmlDb.Xdm.Nodes;

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
    private IDocument? _subscribedDocument;

    /// <summary>Initializes a new <see cref="EditorMainWindow"/>.</summary>
    public EditorMainWindow()
    {
        _window = new MainWindow();

        _treeView = new TreeEditorView();
        _textView = new TextEditorView { IsReadOnly = true };

        var split = new Platform.Shell.SplitView
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Pane1Size = 360,
            Pane1Content = _treeView,
            Pane2Content = _textView,
        };

        // Wire F2 inline rename — routes to element or attribute command based on node kind.
        _treeView.NodeRenameRequested += (node, newLabel) =>
        {
            if (_state.ActiveDocument is not XmlDocument doc) return;
            if (node is not XmlTreeNode xmlNode) return;

            IEditorCommand? command =
                xmlNode.XdmNode switch
                {
                    XdmElement el => new RenameElementCommand(doc, el.Id, newLabel),
                    XdmAttribute attr => new RenameAttributeCommand(doc, attr.Id, newLabel),
                    _ => null,
                };

            if (command is not null)
                doc.Undo.Execute(command);
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

    private void OnSubscribedDocumentChanged(DocumentChange change)
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
