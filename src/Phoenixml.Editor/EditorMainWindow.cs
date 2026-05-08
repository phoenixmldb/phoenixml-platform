using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Phoenixml.Editor.Commands;
using Phoenixml.Editor.Menus;
using Phoenixml.Editor.Quit;
using Phoenixml.Editor.Settings;
using Phoenixml.Editor.Validation;
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
    private readonly EditorValidationService _validation;
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

        _validation = new EditorValidationService(new SchemaResolver(Settings));
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
        // Build the inner Grid: Col 0 = explorer, Col 1 = 4-dip splitter, Col 2 = node list.
        // The initial explorer width is read from the default settings value (250 dip).
        // LoadAsync is in-flight at this point; the persisted width applies on the next
        // launch once the round-trip save completes. Documented v0.6 trade-off.
        var initialExplorerWidth = Settings.Settings.ExplorerWidth;
        var innerGrid = new Grid();
        innerGrid.ColumnDefinitions.Add(
            new ColumnDefinition(initialExplorerWidth, GridUnitType.Pixel) { MinWidth = 0 });
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition(4, GridUnitType.Pixel));
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        Explorer = new Phoenixml.Editor.Explorer.EditorExplorer(this);
        Grid.SetColumn(Explorer, 0);
        innerGrid.Children.Add(Explorer);

        // GridSplitter between Col 0 (explorer) and Col 2 (node list).
        // Dragging to ~0 effectively hides the explorer; no separate collapse button
        // is provided in v0.6 — the splitter is the resize affordance.
        var splitter = new GridSplitter
        {
            Width = 4,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4A4A4F")),
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        };
        Grid.SetColumn(splitter, 1);
        innerGrid.Children.Add(splitter);

        Grid.SetColumn(_nodeList, 2);
        innerGrid.Children.Add(_nodeList);

        // Persist the explorer width each time the user finishes a drag.
        splitter.DragCompleted += (_, _) =>
        {
            var w = innerGrid.ColumnDefinitions[0].ActualWidth;
            Settings.UpdateExplorerWidth(w);
        };

        // Build the ProblemsView and dock it at the bottom (120 dip).
        var problems = new ProblemsView();
        var problemsContainer = new DockPanel { Height = 120 };
        DockPanel.SetDock(problemsContainer, Dock.Bottom);
        problemsContainer.Children.Add(problems);

        // Wire validation: track the active document and push diagnostics to the view.
        _state.ActiveDocumentChanged += doc => _validation.SetActiveDocument(doc);
        _validation.DiagnosticsChanged += diags => problems.SetDiagnostics(diags);

        // Wire click-to-navigate: double-click a diagnostic row → select the first matching element.
        problems.DiagnosticActivated += diag =>
        {
            if (_state.ActiveDocument is not XmlDocument doc) return;
            var elementName = ExtractQuotedElementName(diag.Message);
            if (elementName is null) return;
            var found = FindFirstElementByName(doc, elementName);
            if (found is not null) _nodeList.SelectById(found.Id.ToString());
        };

        // Order: menu, toolbar, addressBar (top); problemsContainer (bottom); innerGrid fills.
        // innerGrid contains the explorer (Col 0), the GridSplitter (Col 1), and
        // the node list (Col 2) so both the explorer and the node list share the
        // remaining space after the top/bottom dock areas are consumed.
        _rootPanel.Children.Add(menu);
        _rootPanel.Children.Add(toolbar);
        _rootPanel.Children.Add(addressBar);
        _rootPanel.Children.Add(problemsContainer);
        _rootPanel.Children.Add(innerGrid);

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

        // Hook window close: if any tabs have unsaved changes, prompt the user before
        // allowing the close to proceed.
        _window.Closing += async (_, args) =>
        {
            var dirtyTabs = _state.Tabs.Where(t => t.Document.IsDirty).ToList();
            if (dirtyTabs.Count == 0) return;  // nothing unsaved — allow close

            args.Cancel = true;  // pause the close while we ask the user

            var dialog = new DirtyDocsConfirmDialog(
                dirtyTabs.Select(t => t.Document.DisplayName).ToList());
            await dialog.ShowDialog(_window);

            switch (dialog.Outcome)
            {
                case DirtyDocsConfirmDialog.Result.SaveAll:
                    foreach (var t in dirtyTabs)
                    {
                        if (t.Document.Source is null) continue;  // never-saved — skip for v0.6
                        try { await t.Document.SaveAsync(CancellationToken.None); }
                        catch (System.IO.IOException) { /* save failed — continue silently for v0.6 */ }
                        catch (UnauthorizedAccessException) { }
                    }
                    // Re-trigger close. Dirty docs that were saved are now clean; the second
                    // pass through this handler returns early (Count == 0) and Cancel stays false.
                    _window.Close();
                    break;

                case DirtyDocsConfirmDialog.Result.DiscardAll:
                    // MarkClean is a small lie: we're not persisting. Safe because the
                    // editor exits immediately; the second pass sees no dirty tabs.
                    foreach (var t in dirtyTabs) t.Document.MarkClean();
                    _window.Close();
                    break;

                case DirtyDocsConfirmDialog.Result.Cancel:
                    // args.Cancel = true already set — window stays open.
                    break;
            }
        };

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
            Settings.RecordRecentFile(path);
        }
        catch (System.IO.IOException) { /* file gone or locked — silent skip */ }
        catch (UnauthorizedAccessException) { /* permission — silent skip */ }
        catch (System.Xml.XmlException) { /* malformed XML — silent skip */ }
    }

    private void UpdateStatusBar()
    {
        var doc = _state.ActiveDocument;
        var label = doc is null ? "(no document)" : $"{doc.DisplayName}";
        var dirtyCount = _state.Tabs.Count(t => t.Document.IsDirty);

        var segments = new List<StatusBarSegment>
        {
            new(Id: "active-document", Label: label, Order: 0),
        };

        if (dirtyCount > 0)
        {
            segments.Add(new StatusBarSegment(
                Id: "dirty-count",
                Label: dirtyCount == 1 ? "1 modified" : $"{dirtyCount} modified",
                Order: 1));
        }

        _window.SetStatusSegments(segments, EmptyServiceProvider.Instance);
    }

    private readonly DockPanel _rootPanel;

    /// <summary>Gets the Avalonia <see cref="Avalonia.Controls.Window"/> to assign as the desktop main window.</summary>
    public Window Window => _window;

    /// <summary>Gets the editor view-model state (active document, etc.).</summary>
    internal EditorState State => _state;

    /// <summary>The left-column file explorer. Surface for the address bar (paste a folder path)
    /// and other consumers that want to retarget the explorer's root.</summary>
    internal Phoenixml.Editor.Explorer.EditorExplorer Explorer { get; private set; } = null!;

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

    /// <summary>
    /// Extracts the first single-quoted token from a diagnostic message.
    /// XSD validator messages commonly quote element/attribute names as <c>'foo'</c>.
    /// Returns <see langword="null"/> when no such token is present.
    /// </summary>
    /// <remarks>
    /// Best-effort: .NET's <c>XmlSchemaValidator</c> messages vary in format; not all
    /// mention the offending node name in single quotes. Misses are silently ignored.
    /// </remarks>
    private static string? ExtractQuotedElementName(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            message, "'([A-Za-z_][\\w.-]*)'");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Breadth-first search for the first <see cref="XdmElement"/> whose
    /// <see cref="XdmElement.LocalName"/> equals <paramref name="localName"/>.
    /// Returns <see langword="null"/> when no match is found.
    /// </summary>
    /// <remarks>
    /// Precise diagnostic-to-node mapping (including line/column source positions) is
    /// deferred to Plan 13. This BFS gives a useful first match in the common case
    /// where the schema error names a single element type.
    /// </remarks>
    private static XdmElement? FindFirstElementByName(XmlDocument doc, string localName)
    {
        var docElementId = doc.Root.DocumentElement;
        if (docElementId is null) return null;

        var queue = new Queue<PhoenixmlDb.Core.NodeId>();
        queue.Enqueue(docElementId.Value);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (doc.Resolve(id) is not XdmElement el) continue;

            if (string.Equals(el.LocalName, localName, StringComparison.Ordinal))
                return el;

            foreach (var childId in el.Children)
                queue.Enqueue(childId);
        }

        return null;
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
