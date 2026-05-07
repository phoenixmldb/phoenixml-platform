using Avalonia.Controls;
using Avalonia.Input;
using Phoenixml.Editor.Commands;
using Phoenixml.Editor.Menus;
using Phoenixml.Editor.Settings;
using Phoenixml.Editor.Tabs;
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
/// injects the XmlNotepad-style <see cref="NodeListView"/> (names on the left, values on
/// the right) as the primary editor surface.
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
    private readonly NodeListView _nodeList;
    private IDocument? _subscribedDocument;

    /// <summary>
    /// Gets the persisted editor settings store. Menus and commands read from and
    /// record to this store. Loaded asynchronously on construction; the menu rebuilds
    /// when <see cref="EditorSettingsStore.Changed"/> fires after load completes.
    /// </summary>
    internal EditorSettingsStore Settings { get; } = new();

    /// <summary>Initializes a new <see cref="EditorMainWindow"/>.</summary>
    public EditorMainWindow()
    {
        _window = new MainWindow();

        // Start loading persisted settings in the background. The Recent Files menu
        // subscribes to Settings.Changed and will rebuild once the load completes.
        _ = Settings.LoadAsync(CancellationToken.None);

        _nodeList = new NodeListView();

        // Wire F2 inline rename — routes to element or attribute command based on node kind.
        _nodeList.NodeRenameRequested += (node, newLabel) =>
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

        // Wire value-column inline edit — routes by node kind.
        _nodeList.NodeValueChangeRequested += (node, newValue) =>
        {
            if (_state.ActiveDocument is not XmlDocument doc) return;
            if (node is not XmlTreeNode xmlNode) return;

            IEditorCommand? command = xmlNode.XdmNode switch
            {
                XdmAttribute attr => new SetAttributeValueCommand(doc, attr.Id, newValue),
                XdmElement el when IsTextEditableElement(el, doc) =>
                    new SetTextContentCommand(doc, el.Id, newValue),
                // Container / mixed-content element → silently ignore. The user sees the
                // pre-edit value reappear when the row rebuilds.
                _ => null,
            };

            if (command is not null)
                doc.Undo.Execute(command);
        };

        _nodeList.NodeInsertChildElementRequested += node =>
        {
            if (_state.ActiveDocument is not XmlDocument doc) return;
            if (node is not XmlTreeNode xmlNode) return;
            if (xmlNode.XdmNode is not XdmElement el) return;
            // Auto-expand the parent so the new child row is visible.
            _nodeList.ExpandById(xmlNode.Id);
            var cmd = new InsertElementCommand(doc, el.Id, "newElement");
            doc.Undo.Execute(cmd);
            _nodeList.BeginRenameById(cmd.InsertedId.ToString());
        };

        _nodeList.NodeInsertAttributeRequested += node =>
        {
            if (_state.ActiveDocument is not XmlDocument doc) return;
            if (node is not XmlTreeNode xmlNode) return;
            if (xmlNode.XdmNode is not XdmElement el) return;
            _nodeList.ExpandById(xmlNode.Id);
            var cmd = new InsertAttributeCommand(doc, el.Id, "newAttr", string.Empty);
            doc.Undo.Execute(cmd);
            _nodeList.BeginRenameById(cmd.InsertedId.ToString());
        };

        _nodeList.NodeDeleteRequested += node =>
        {
            if (_state.ActiveDocument is not XmlDocument doc) return;
            if (node is not XmlTreeNode xmlNode) return;
            if (xmlNode.XdmNode is XdmDocument) return;

            try
            {
                doc.Undo.Execute(new DeleteNodeCommand(doc, xmlNode.XdmNode.Id));
            }
            catch (InvalidOperationException) { /* root delete attempt — ignore */ }
        };

        _rootPanel = new DockPanel { LastChildFill = true };

        var menu = new Menu
        {
            ItemsSource = new[] { FileMenu.Build(this), EditMenu.Build(this) },
        };
        DockPanel.SetDock(menu, Dock.Top);
        var toolbar = new Phoenixml.Editor.Toolbar.EditorToolbar(this);
        DockPanel.SetDock(toolbar, Dock.Top);
        var addressBar = new Phoenixml.Editor.Toolbar.EditorAddressBar(this);
        DockPanel.SetDock(addressBar, Dock.Top);
        var tabStrip = new EditorTabStrip(_state);
        DockPanel.SetDock(tabStrip, Dock.Top);
        // Order: menu, toolbar, addressBar, tabStrip, nodeList (LastChildFill takes the rest).
        _rootPanel.Children.Add(menu);
        _rootPanel.Children.Add(toolbar);
        _rootPanel.Children.Add(addressBar);
        _rootPanel.Children.Add(tabStrip);
        _rootPanel.Children.Add(_nodeList);

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

    /// <summary>
    /// Opens a file at startup (called from <see cref="App.OnFrameworkInitializationCompleted"/>
    /// when a path is passed as a command-line argument). Fire-and-forget; failures are silent.
    /// </summary>
    public void OpenFileOnLaunch(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _ = OpenFileOnLaunchAsync(path);
    }

    private async Task OpenFileOnLaunchAsync(string path)
    {
        try
        {
            var doc = await Phoenixml.Platform.Editor.Xml.XmlDocument
                .LoadAsync(path, CancellationToken.None);
            _state.OpenDocument(doc);
        }
        catch (System.IO.IOException) { /* file gone or locked — silent skip */ }
        catch (UnauthorizedAccessException) { /* permission — silent skip */ }
        catch (System.Xml.XmlException) { /* malformed XML — silent skip */ }
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
            _nodeList.SetProvider(null);
            _window.SetTitle("Phoenixml Editor");
            UpdateStatusBar();
            return;
        }

        _nodeList.SetProvider(doc.TreeNodes);

        _window.SetTitle($"Phoenixml Editor — {doc.DisplayName}");
        UpdateStatusBar();

        doc.Changed += OnSubscribedDocumentChanged;
    }

    private void OnSubscribedDocumentChanged(DocumentChange change)
    {
        // The NodeListView refreshes itself via XmlTreeNodeProvider.Changed (which fires on
        // Document.Changed). Only the status bar needs an explicit update here.
        UpdateStatusBar();
    }

    // Predicate matching SetTextContentCommand's preconditions: the element has no element
    // children. Empty elements ARE editable since Plan 10 (SetTextContentCommand allocates a
    // fresh XdmText child on the empty path). Mixed/container elements stay rejected.
    private static bool IsTextEditableElement(XdmElement el, XmlDocument doc)
    {
        foreach (var childId in el.Children)
        {
            if (doc.Resolve(childId) is XdmElement) return false;
        }
        return true;
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
