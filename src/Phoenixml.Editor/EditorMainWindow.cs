using System.Collections.Generic;
using System.Xml.Schema;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Phoenixml.Editor.Clipboard;
using Phoenixml.Editor.Commands;
using Phoenixml.Editor.Find;
using Phoenixml.Editor.Menus;
using Phoenixml.Editor.Quit;
using Phoenixml.Editor.Settings;
using Phoenixml.Editor.Validation;
using Phoenixml.Editor.ViewModels;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Abstractions.Editing;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Clipboard;
using Phoenixml.Platform.Editor.Xml.Editing;
using Phoenixml.Platform.Editor.Xml.Navigation;
using Phoenixml.Platform.Editor.Xml.Tree;
using Phoenixml.Platform.Editor.Xml.Validation;
using Phoenixml.Platform.Editing;
using Phoenixml.Platform;
using Phoenixml.Platform.Shell;
using PhoenixmlDb.Xdm.Nodes;
using NodeId = PhoenixmlDb.Core.NodeId;

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
public sealed class EditorMainWindow : IFindHost
{
    private readonly MainWindow _window;
    private readonly EditorState _state = new();
    private readonly NodeListView _nodeList;
    private readonly EditorValidationService _validation;
    private readonly DiagnosticIndex _diagIndex = new();
    private readonly MatchIndex _matchIndex = new();
    private readonly FindBar _findBar = new() { IsVisible = false };
    private readonly FindController _findController;
    private readonly SchemaCache _schemaCache = new();
    private readonly BreadcrumbView _breadcrumb = new();
    private IDocument? _subscribedDocument;

    /// <summary>
    /// XPath of the currently selected node, surfaced by the status bar.
    /// Empty when no document is active or no node is selected.
    /// </summary>
    private string _selectedXPath = string.Empty;

    /// <summary>
    /// Clipboard service, initialized once the window's TopLevel is available (on Opened).
    /// Null before the window is shown; clipboard handlers no-op when null.
    /// </summary>
    private ClipboardService? _clipboard;

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

        _validation = new EditorValidationService(new SchemaResolver(Settings, _schemaCache));
        _nodeList = new NodeListView();

        // FindController must come after _nodeList so the IFindHost implementation
        // (which references _nodeList) is ready before any events fire.
        _findController = new FindController(this, _matchIndex);
        _findBar.QueryChanged += (q, mc) => _findController.OnQueryChanged(q, mc);
        _findBar.NextRequested += _findController.OnNext;
        _findBar.PreviousRequested += _findController.OnPrevious;
        _findBar.CloseRequested += _findController.OnClose;
        _findBar.ReplaceCurrentRequested += rep => _findController.OnReplaceCurrent(rep);
        _findBar.ReplaceAllRequested += rep => _findController.OnReplaceAll(rep);

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

        // Populate insert options from the active schema before the context menu opens.
        _nodeList.BuildingInsertOptions += (node, options) =>
        {
            if (_state.ActiveDocument is not XmlDocument doc) return;
            if (node is not XmlTreeNode xmlNode) return;
            if (xmlNode.XdmNode is not XdmElement) return;

            var schemas = new SchemaResolver(Settings, _schemaCache).Resolve(doc);
            if (schemas is null) return;

            var resolved = SchemaInsertOptionsService.Resolve(doc, xmlNode.XdmNode.Id, schemas);
            options.ChildElementNames = resolved.ChildElementNames;
            options.AttributeNames = resolved.AttributeNames;
        };

        // Schema-aware Insert Child Element handler.
        _nodeList.NodeInsertChildElementWithNameRequested += (node, chosenName) =>
        {
            if (_state.ActiveDocument is not XmlDocument doc) return;
            if (node is not XmlTreeNode xmlNode) return;
            if (xmlNode.XdmNode is not XdmElement el) return;

            _nodeList.ExpandById(xmlNode.Id);
            var cmd = new InsertElementCommand(doc, el.Id, chosenName);
            doc.Undo.Execute(cmd);

            var insertedIdStr = cmd.InsertedId.ToString();
            var schemas = new SchemaResolver(Settings, _schemaCache).Resolve(doc);
            if (schemas is not null && IsSimpleTypedElement(doc, cmd.InsertedId, schemas))
                _nodeList.BeginValueEditById(insertedIdStr);
            else
                _nodeList.SelectById(insertedIdStr);
        };

        // Schema-aware Insert Attribute handler — attributes always auto-value-edit.
        _nodeList.NodeInsertAttributeWithNameRequested += (node, chosenName) =>
        {
            if (_state.ActiveDocument is not XmlDocument doc) return;
            if (node is not XmlTreeNode xmlNode) return;
            if (xmlNode.XdmNode is not XdmElement el) return;

            _nodeList.ExpandById(xmlNode.Id);
            var cmd = new InsertAttributeCommand(doc, el.Id, chosenName, string.Empty);
            doc.Undo.Execute(cmd);
            _nodeList.BeginValueEditById(cmd.InsertedId.ToString());
        };

        // Drag-and-drop reorder: build a MoveSubtreeCommand from the requested move.
        // Silently ignore drops that would create a cycle or otherwise violate invariants
        // (e.g. dragging the document root, which has no parent for the BeforeTarget /
        // AfterTarget cases).
        _nodeList.NodeReorderRequested += (source, target, pos) =>
        {
            if (_state.ActiveDocument is not XmlDocument doc) return;
            if (source is not XmlTreeNode srcNode || target is not XmlTreeNode tgtNode) return;
            if (srcNode.XdmNode is not XdmElement srcElement) return;
            if (tgtNode.XdmNode is not XdmElement tgtElement) return;

            NodeId targetParentId;
            int insertIndex;
            switch (pos)
            {
                case DropPosition.AsLastChild:
                    targetParentId = tgtElement.Id;
                    insertIndex = tgtElement.Children.Count;
                    break;
                case DropPosition.BeforeTarget:
                case DropPosition.AfterTarget:
                    if (tgtElement.Parent is not { } tgtParent) return;
                    if (doc.Resolve(tgtParent) is not XdmElement tgtParentEl) return;
                    targetParentId = tgtParent;
                    var idx = -1;
                    for (int i = 0; i < tgtParentEl.Children.Count; i++)
                    {
                        if (tgtParentEl.Children[i] == tgtElement.Id) { idx = i; break; }
                    }
                    if (idx < 0) return;
                    insertIndex = pos == DropPosition.BeforeTarget ? idx : idx + 1;
                    break;
                default:
                    return;
            }

            try
            {
                doc.Undo.Execute(new MoveSubtreeCommand(doc, srcElement.Id, targetParentId, insertIndex));
            }
            catch (InvalidOperationException)
            {
                // Cycle prevention or other invariant violation — silent reject (the cursor's
                // drag-effect indicator already conveyed "not allowed" to the user).
            }
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
        Explorer.ApplySchemaRequested += xsdPath =>
        {
            if (_state.ActiveDocument is not XmlDocument doc) return;
            if (doc.Source is not Uri source) return;
            Settings.SetPerDocumentSchema(source.LocalPath, xsdPath);
            _validation.Revalidate();
        };
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

        // Wrap the find bar + breadcrumb + node list in a DockPanel so each can dock-top
        // above the list. Col 2; find bar starts hidden, breadcrumb starts empty.
        var rightColumn = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_findBar, Dock.Top);
        DockPanel.SetDock(_breadcrumb, Dock.Top);
        rightColumn.Children.Add(_findBar);
        rightColumn.Children.Add(_breadcrumb);
        rightColumn.Children.Add(_nodeList);

        // Wire selection → status-bar XPath + breadcrumb path. Read-only navigation feature
        // (Plan 20). XPath is recomputed every change; the breadcrumb is rebuilt from the
        // selected node's ancestor chain (each segment carries the ancestor NodeId).
        _nodeList.SelectionChanged += OnNodeListSelectionChanged;

        // Clicking a breadcrumb segment navigates the tree to that ancestor. The payload
        // stashed by OnNodeListSelectionChanged is the segment's NodeId string; we forward
        // it to NodeListView.SelectById, which mirrors how diagnostics jump.
        _breadcrumb.SegmentClicked += segment =>
        {
            if (segment.Payload is string nodeIdStr && !string.IsNullOrEmpty(nodeIdStr))
                _nodeList.EnsureVisibleAndSelect(nodeIdStr);
        };
        Grid.SetColumn(rightColumn, 2);
        innerGrid.Children.Add(rightColumn);

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

        // Wire validation: track the active document and push diagnostics to the view
        // and to the DiagnosticIndex so tree rows can show inline error glyphs.
        _state.ActiveDocumentChanged += doc => _validation.SetActiveDocument(doc);
        _validation.DiagnosticsChanged += diags =>
        {
            _diagIndex.Reset(diags);
            problems.SetDiagnostics(diags);
            // Trigger a NodeListView rebuild so IconKey changes (via DiagnosticIndex)
            // propagate to all visible rows. The provider's RaiseChanged fires Changed,
            // which NodeListView handles the same way as Document.Changed.
            if (_state.ActiveDocument is XmlDocument activeDoc)
                activeDoc.TreeNodes.TriggerRefresh();
        };

        // Wire click-to-navigate: double-click a diagnostic row → select the offending node.
        // Precise path: use the parsed Location.NodeId from Plan 13 (B2/B3).
        // Fallback path: regex heuristic from Plan 12 for diagnostics whose Location
        // couldn't be mapped (e.g. document-level errors with no associated node).
        problems.DiagnosticActivated += diag =>
        {
            if (diag.Location is DiagnosticLocation loc)
            {
                // Use EnsureVisibleAndSelect so collapsed ancestors expand on the way to the
                // offending node — diagnostics often target nodes deep inside a folded tree.
                _nodeList.EnsureVisibleAndSelect(loc.NodeId.ToString());
                return;
            }

            if (_state.ActiveDocument is not XmlDocument doc) return;
            var elementName = ExtractQuotedElementName(diag.Message);
            if (elementName is null) return;
            var found = FindFirstElementByName(doc, elementName);
            if (found is not null) _nodeList.EnsureVisibleAndSelect(found.Id.ToString());
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
        _window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.F, KeyModifiers.Control),
            Command = new RelayCommand(ShowFindBar),
        });
        _window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.H, KeyModifiers.Control),
            Command = new RelayCommand(ShowFindBarInReplaceMode),
        });
        _window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.C, KeyModifiers.Control),
            Command = new RelayCommand(async () => await CopySelectionAsync()),
        });
        _window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.X, KeyModifiers.Control),
            Command = new RelayCommand(async () => await CutSelectionAsync()),
        });
        _window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.V, KeyModifiers.Control),
            Command = new RelayCommand(async () => await PasteAsync()),
        });

        // Initialize ClipboardService once the window's TopLevel is realized.
        // Opened fires after the window is fully attached to the visual tree,
        // guaranteeing TopLevel.Clipboard is non-null.
        _window.Opened += (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(_window);
            if (topLevel?.Clipboard is IClipboard clip)
                _clipboard = ClipboardService.FromAvalonia(clip);
        };

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
                    foreach (var t in dirtyTabs)
                    {
                        if (t.Document.Source is Uri src && System.IO.File.Exists(src.LocalPath))
                        {
                            try
                            {
                                var fresh = await Phoenixml.Platform.Editor.Xml.XmlDocument
                                    .LoadAsync(src.LocalPath, CancellationToken.None);
                                _state.ReplaceDocument(t.Document, fresh);
                            }
                            catch (System.IO.IOException) { t.Document.MarkClean(); }
                            catch (System.Xml.XmlException) { t.Document.MarkClean(); }
                            catch (UnauthorizedAccessException) { t.Document.MarkClean(); }
                        }
                        else
                        {
                            // Never-saved doc — no source to reload from. MarkClean is the only option.
                            t.Document.MarkClean();
                        }
                    }
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

        if (!string.IsNullOrEmpty(_selectedXPath))
        {
            segments.Add(new StatusBarSegment(
                Id: "selected-xpath",
                Label: _selectedXPath,
                Order: 2));
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

    /// <summary>
    /// The validation service for the active document.
    /// Exposed so menu commands can request an immediate re-validation pass via
    /// <see cref="Validation.EditorValidationService.Revalidate"/> after schema changes
    /// that don't mutate the document content.
    /// </summary>
    internal EditorValidationService Validation => _validation;

    private void OnActiveDocumentChanged(XmlDocument? doc)
    {
        // Unsubscribe from any previously active document.
        if (_subscribedDocument is not null)
            _subscribedDocument.Changed -= OnSubscribedDocumentChanged;
        _subscribedDocument = doc;

        // Reset selection-derived UI: empty the breadcrumb and drop the XPath segment so
        // stale state from the previous document doesn't bleed into the new one.
        _selectedXPath = string.Empty;
        _breadcrumb.SetPath(Array.Empty<BreadcrumbSegment>());

        if (doc is null)
        {
            _nodeList.SetProvider(null);
            _window.SetTitle("Phoenixml Editor");
            UpdateStatusBar();
            return;
        }

        // Attach the diagnostic index to the new provider so XmlTreeNode.IconKey
        // can reflect the latest validation state.
        doc.TreeNodes.DiagnosticIndex = _diagIndex;
        // Attach the match index so XmlTreeNode.MatchHighlight reflects find results.
        doc.TreeNodes.MatchIndex = _matchIndex;
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

    /// <summary>
    /// Selection-driven update for Plan 20's breadcrumb + status-bar XPath.
    /// Recomputes the XPath and rebuilds the ancestor breadcrumb path on every selection
    /// change. Read-only — never mutates the document.
    /// </summary>
    /// <remarks>
    /// The breadcrumb's segments mirror the XPath steps in order. Each segment's
    /// <see cref="BreadcrumbSegment.Payload"/> is the ancestor's NodeId as a string so
    /// the click handler can call <c>NodeListView.EnsureVisibleAndSelect</c> directly.
    /// </remarks>
    private void OnNodeListSelectionChanged(Phoenixml.Platform.Abstractions.Editing.ITreeNode node)
    {
        if (_state.ActiveDocument is not XmlDocument doc)
        {
            _selectedXPath = string.Empty;
            _breadcrumb.SetPath(Array.Empty<BreadcrumbSegment>());
            UpdateStatusBar();
            return;
        }

        if (node is not XmlTreeNode xmlNode)
        {
            _selectedXPath = string.Empty;
            _breadcrumb.SetPath(Array.Empty<BreadcrumbSegment>());
            UpdateStatusBar();
            return;
        }

        _selectedXPath = XPathBuilder.Build(doc, xmlNode.XdmNode.Id);
        _breadcrumb.SetPath(BuildBreadcrumbSegments(doc, xmlNode.XdmNode));
        UpdateStatusBar();
    }

    /// <summary>
    /// Builds the ordered breadcrumb segments for <paramref name="target"/> by walking up
    /// via <see cref="XdmNode.Parent"/>. The first segment is always the document root
    /// (<c>/</c>); subsequent segments mirror the XPath steps. Each segment's Payload is
    /// the ancestor's NodeId as a string, ready for <c>NodeListView.SelectById</c>.
    /// </summary>
    private static List<BreadcrumbSegment> BuildBreadcrumbSegments(
        XmlDocument doc, XdmNode target)
    {
        var chain = new List<XdmNode>();
        XdmNode? cursor = target;
        while (cursor is not null && cursor is not XdmDocument)
        {
            chain.Add(cursor);
            cursor = cursor.Parent is { } p ? doc.Resolve(p) : null;
        }
        chain.Reverse();

        var segments = new List<BreadcrumbSegment>
        {
            new(Id: doc.Root.Id.ToString(),
                Label: "/",
                Payload: doc.Root.DocumentElement?.ToString() ?? doc.Root.Id.ToString()),
        };

        foreach (var n in chain)
        {
            var label = SegmentLabel(n);
            segments.Add(new BreadcrumbSegment(
                Id: n.Id.ToString(),
                Label: label,
                Payload: n.Id.ToString()));
        }

        return segments;
    }

    private static string SegmentLabel(XdmNode node) => node switch
    {
        XdmElement el => el.LocalName,
        XdmAttribute a => "@" + a.LocalName,
        XdmText _ => "text()",
        XdmComment _ => "comment()",
        XdmProcessingInstruction pi => $"pi('{pi.Target}')",
        _ => node.GetType().Name,
    };

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
    /// Returns <see langword="true"/> when the element identified by <paramref name="elementId"/>
    /// is declared in <paramref name="schemas"/> with a simple type or a complex type whose
    /// content type is <see cref="XmlSchemaContentType.TextOnly"/>.
    /// </summary>
    /// <remarks>
    /// Used post-insert to decide whether to drop the user directly into value-edit mode.
    /// <c>TextOnly</c> means the complex type allows text content only (attributes permitted),
    /// which behaves identically to a simple type for value-editing purposes.
    /// <c>Mixed</c> content is intentionally excluded — auto-entering a value cell on a
    /// mixed-content element would be confusing.
    /// </remarks>
    private static bool IsSimpleTypedElement(
        XmlDocument doc,
        PhoenixmlDb.Core.NodeId elementId,
        XmlSchemaSet schemas)
    {
        if (SchemaElementResolver.Resolve(doc, elementId, schemas) is not XmlSchemaElement schemaElement)
            return false;
        return schemaElement.ElementSchemaType switch
        {
            XmlSchemaSimpleType => true,
            XmlSchemaComplexType c => c.ContentType == XmlSchemaContentType.TextOnly,
            _ => false,
        };
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

    // -------------------------------------------------------------------------
    // Find bar
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows the find bar and focuses its query TextBox.
    /// If the bar is already visible, re-focuses and selects all text so the user can
    /// type a new query without clearing manually (standard text-editor pattern).
    /// </summary>
    internal void ShowFindBar()
    {
        _findBar.IsVisible = true;
        _findBar.FocusQueryBox();
    }

    /// <summary>
    /// Shows the find bar in Replace mode (expands the Replace row and focuses the Replace
    /// TextBox). Triggered by Ctrl+H. Standard text-editor binding.
    /// </summary>
    internal void ShowFindBarInReplaceMode()
    {
        _findBar.IsVisible = true;
        _findBar.OpenReplaceMode();
    }

    /// <summary>
    /// Hides the find bar and resets its state (clears text and counter).
    /// Called by <see cref="FindController.OnClose"/> via <see cref="IFindHost"/>.
    /// </summary>
    internal void HideFindBar()
    {
        _findBar.IsVisible = false;
        _findBar.Reset();
    }

    // -------------------------------------------------------------------------
    // Clipboard handlers (Ctrl+C / Ctrl+X / Ctrl+V)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Copies the selected element and its entire descendant subtree to the clipboard as XML.
    /// No-ops when there is no active document, no selection, or the selected node is not an element.
    /// </summary>
    private async Task CopySelectionAsync()
    {
        if (_clipboard is null) return;
        if (_state.ActiveDocument is not XmlDocument doc) return;
        if (_nodeList.SelectedNode is not XmlTreeNode xmlNode) return;
        if (xmlNode.XdmNode is not XdmElement el) return;

        var xml = SubtreeSerializer.Serialize(doc, el.Id);
        await _clipboard.WriteXmlAsync(xml);
    }

    /// <summary>
    /// Copies the selected element subtree to the clipboard (same as Ctrl+C) then deletes the
    /// source element via <see cref="DeleteNodeCommand"/> so the operation is a single undo step.
    /// The clipboard write is not part of the undo stack — Ctrl+Z restores the node while the
    /// clipboard retains the XML, matching standard text-editor Cut behaviour.
    /// </summary>
    private async Task CutSelectionAsync()
    {
        if (_clipboard is null) return;
        if (_state.ActiveDocument is not XmlDocument doc) return;
        if (_nodeList.SelectedNode is not XmlTreeNode xmlNode) return;
        if (xmlNode.XdmNode is not XdmElement el) return;

        var xml = SubtreeSerializer.Serialize(doc, el.Id);
        await _clipboard.WriteXmlAsync(xml);

        try
        {
            doc.Undo.Execute(new DeleteNodeCommand(doc, el.Id));
        }
        catch (InvalidOperationException) { /* document root delete attempt — ignore */ }
    }

    /// <summary>
    /// Reads XML from the clipboard and inserts it as the last child of the currently selected
    /// element via <see cref="InsertSubtreeCommand"/>. Silently ignores malformed clipboard XML
    /// and non-element selections (Plan 19 will surface parse errors in the status bar).
    /// </summary>
    private async Task PasteAsync()
    {
        if (_clipboard is null) return;
        if (_state.ActiveDocument is not XmlDocument doc) return;
        if (_nodeList.SelectedNode is not XmlTreeNode xmlNode) return;
        if (xmlNode.XdmNode is not XdmElement parent) return;

        var xml = await _clipboard.ReadXmlAsync();
        if (string.IsNullOrEmpty(xml)) return;

        try
        {
            var cmd = new InsertSubtreeCommand(doc, parent.Id, xml);
            doc.Undo.Execute(cmd);
            _nodeList.ExpandById(xmlNode.Id);
        }
        catch (System.Xml.XmlException) { /* malformed clipboard XML — silent for v0.11 */ }
        catch (InvalidOperationException) { /* parent not element / invariant failure */ }
    }

    // -------------------------------------------------------------------------
    // IFindHost explicit implementation
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    XmlDocument? IFindHost.ActiveDocument => _state.ActiveDocument;

    /// <inheritdoc/>
    XmlTreeNodeProvider? IFindHost.ActiveProvider =>
        _state.ActiveDocument?.TreeNodes as XmlTreeNodeProvider;

    /// <inheritdoc/>
    void IFindHost.EnsureVisibleAndSelect(string nodeId) =>
        _nodeList.EnsureVisibleAndSelect(nodeId);

    /// <inheritdoc/>
    void IFindHost.HideFindBar() => HideFindBar();

    /// <inheritdoc/>
    void IFindHost.SetMatchCount(int active, int total) =>
        _findBar.SetMatchCount(active, total);
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
