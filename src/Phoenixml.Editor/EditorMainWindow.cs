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
public sealed class EditorMainWindow : IFindHost, IXPathQueryHost
{
    private readonly MainWindow _window;
    private readonly EditorState _state = new();
    private readonly NodeListView _nodeList;
    private readonly Views.TextEditorView _textEditor = new();
    private readonly Views.OutlineView _outlineView = new();
    private readonly Views.ReferencesView _referencesView = new();
    private ContentControl _centralPane = null!;
    private readonly Lsp.LspServerRegistry _lspRegistry = new(AppContext.BaseDirectory);
    private readonly Dictionary<ViewModels.DocumentTab, Lsp.LspSession> _lspSessions = new();
    private readonly Dictionary<ViewModels.DocumentTab, int> _lspRestartAttempts = new();
    private ProblemsView? _problemsView;
    private readonly EditorValidationService _validation;
    private readonly DiagnosticIndex _diagIndex = new();
    private readonly MatchIndex _matchIndex = new();
    private readonly FindBar _findBar = new() { IsVisible = false };
    private readonly FindController _findController;
    private readonly XPathQueryBar _xpathBar = new() { IsVisible = false };
    private readonly XPathQueryController _xpathController;
    private readonly SchemaCache _schemaCache = new();
    private readonly BreadcrumbView _breadcrumb = new();
    private IDocument? _subscribedDocument;

    /// <summary>
    /// XPath of the currently selected node, surfaced by the status bar.
    /// Empty when no document is active or no node is selected.
    /// </summary>
    private string _selectedXPath = string.Empty;

    /// <summary>
    /// Scalar XPath result from the query bar, when the active XPath expression yielded
    /// atomic values instead of nodes. Takes precedence over <see cref="_selectedXPath"/>
    /// in the status bar while the XPath bar is open. Cleared when the bar closes.
    /// </summary>
    private string _xpathScalar = string.Empty;

    /// <summary>
    /// Transient notification segment ("last-action"). Surface silent rejects, paste
    /// confirmations, Copy XPath acks, etc. Auto-clears after 4 seconds.
    /// </summary>
    private readonly Status.LastActionStatus _lastAction = new(TimeSpan.FromSeconds(4));

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

        // Transient status segment fires Changed on notify + auto-clear.
        _lastAction.Changed += UpdateStatusBar;

        // XPath query bar — separate highlight mode, mutually exclusive with FindBar so
        // the two tint sources don't clash. See ShowXPathBar / ShowFindBar for the mutex.
        _xpathController = new XPathQueryController(this, _matchIndex);
        _xpathBar.QuerySubmitted += async xp => await _xpathController.OnQuerySubmittedAsync(xp);
        _xpathBar.PreviousRequested += _xpathController.OnPrevious;
        _xpathBar.CloseRequested += _xpathController.OnClose;
        _xpathBar.HistoryRequested += () => _xpathController.SnapshotHistory();

        // Plan 28 #4: outline symbol selection moves the caret in the text view.
        _outlineView.SymbolActivated += (_, sym) =>
            _textEditor.MoveCaretTo(sym.Range.Start.Line, sym.Range.Start.Character);
        _outlineView.IsVisible = false;

        // Plan 29: references panel. Shares the right dock slot with the outline —
        // Shift+F12 toggles in references, outline auto-restores when the panel is dismissed.
        _referencesView.IsVisible = false;
        _referencesView.ReferenceActivated += (_, loc) =>
            _textEditor.MoveCaretTo(loc.Range.Start.Line, loc.Range.Start.Character);
        _textEditor.ReferencesRequested += (_, refs) =>
        {
            _referencesView.SetReferences(refs);
            _outlineView.IsVisible = false;
            _referencesView.IsVisible = true;
        };

        // Context-aware completions: prefer schema-derived names when a schema is bound;
        // fall back to document-harvested names. Plan 25 #1.
        _xpathBar.NamesRequested += (kind, prefix) =>
        {
            if (_state.ActiveDocument is not XmlDocument doc) return Array.Empty<string>();

            IReadOnlyList<string> all;
            var schemas = new SchemaResolver(Settings, _schemaCache).Resolve(doc);
            if (schemas is not null)
            {
                all = kind switch
                {
                    Find.XPathContextKind.ElementName =>
                        Phoenixml.Platform.Editor.Xml.Validation.SchemaElementNameHarvester.AllReachableElementNames(schemas),
                    Find.XPathContextKind.AttributeName =>
                        Phoenixml.Platform.Editor.Xml.Validation.SchemaElementNameHarvester.AllAttributeNames(schemas),
                    _ => Array.Empty<string>(),
                };
            }
            else
            {
                all = kind switch
                {
                    Find.XPathContextKind.ElementName => Find.XPathContextProvider.CollectElementNames(doc),
                    Find.XPathContextKind.AttributeName => Find.XPathContextProvider.CollectAttributeNames(doc),
                    _ => Array.Empty<string>(),
                };
            }

            if (prefix.Length == 0) return all;
            var filtered = new List<string>();
            foreach (var n in all)
                if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    filtered.Add(n);
            return filtered;
        };

        // Plan 24: persist XPath history across sessions. Load on startup; save on close.
        try
        {
            _xpathController.LoadHistory(Find.XPathHistoryStore.Load(Find.XPathHistoryStore.DefaultPath));
        }
        catch (IOException) { /* permission / disk — silent */ }
        catch (UnauthorizedAccessException) { /* silent */ }

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
            var insertCmd = new InsertElementCommand(doc, el.Id, chosenName);
            doc.Undo.Execute(insertCmd);

            var insertedIdStr = insertCmd.InsertedId.ToString();
            var schemas = new SchemaResolver(Settings, _schemaCache).Resolve(doc);
            if (schemas is not null && IsSimpleTypedElement(doc, insertCmd.InsertedId, schemas))
            {
                // Plan 25 #5: seed a type-appropriate default into the new element so the
                // user sees `0` / `false` / today / first-enum-value instead of an empty cell.
                var schemaEl = Phoenixml.Platform.Editor.Xml.Validation.SchemaElementResolver.Resolve(doc, insertCmd.InsertedId, schemas);
                var defaultText = Phoenixml.Platform.Editor.Xml.Validation.SchemaDefaultValueResolver.GetDefault(schemaEl?.ElementSchemaType);
                if (!string.IsNullOrEmpty(defaultText))
                {
                    doc.Undo.Execute(new SetTextContentCommand(doc, insertCmd.InsertedId, defaultText));
                }
                _nodeList.BeginValueEditById(insertedIdStr);
            }
            else
            {
                _nodeList.SelectById(insertedIdStr);
            }
        };

        // Schema-aware Insert Attribute handler — attributes always auto-value-edit.
        _nodeList.NodeInsertAttributeWithNameRequested += (node, chosenName) =>
        {
            if (_state.ActiveDocument is not XmlDocument doc) return;
            if (node is not XmlTreeNode xmlNode) return;
            if (xmlNode.XdmNode is not XdmElement el) return;

            _nodeList.ExpandById(xmlNode.Id);

            // Plan 25 #5: seed default attribute value from schema type when known.
            var defaultText = string.Empty;
            var schemas = new SchemaResolver(Settings, _schemaCache).Resolve(doc);
            if (schemas is not null
                && Phoenixml.Platform.Editor.Xml.Validation.SchemaElementResolver.Resolve(doc, el.Id, schemas) is XmlSchemaElement schemaParent
                && schemaParent.ElementSchemaType is XmlSchemaComplexType ct
                && ct.AttributeUses[new System.Xml.XmlQualifiedName(chosenName)] is XmlSchemaAttribute schemaAttr)
            {
                defaultText = Phoenixml.Platform.Editor.Xml.Validation.SchemaDefaultValueResolver.GetDefault(schemaAttr.AttributeSchemaType);
            }

            var cmd = new InsertAttributeCommand(doc, el.Id, chosenName, defaultText);
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
            catch (InvalidOperationException ex)
            {
                // Cycle prevention or other invariant violation. The cursor's drag-effect
                // indicator already conveyed "not allowed"; surface the specific reason too.
                NotifyAction($"Move rejected: {ex.Message}");
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

        _nodeList.NodeCopyXPathRequested += async node =>
        {
            if (_clipboard is null) return;
            if (_state.ActiveDocument is not XmlDocument doc) return;
            if (node is not XmlTreeNode xmlNode) return;

            var xpath = XPathBuilder.Build(doc, xmlNode.XdmNode.Id);
            if (string.IsNullOrEmpty(xpath)) return;
            await _clipboard.WriteTextAsync(xpath);
            NotifyAction($"Copied XPath: {xpath}");
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
        // Plan 25 #4: right-click XML → pick XSD via file picker → bind it.
        Explorer.ApplySchemaToXmlRequested += async xmlPath =>
        {
            var topLevel = TopLevel.GetTopLevel(_window);
            if (topLevel is null) return;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(
                new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Apply Schema to " + System.IO.Path.GetFileName(xmlPath),
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new Avalonia.Platform.Storage.FilePickerFileType("XML Schema")
                        {
                            Patterns = ["*.xsd"],
                        },
                    ],
                });
            if (files.Count == 0) return;
            var xsd = files[0].Path.LocalPath;
            Settings.SetPerDocumentSchema(xmlPath, xsd);
            NotifyAction($"Schema applied to {System.IO.Path.GetFileName(xmlPath)}");
            // If the XML file is currently open, re-resolve schema + re-validate.
            if (_state.Tabs.FirstOrDefault(t =>
                string.Equals(t.Document.Source?.LocalPath, xmlPath, StringComparison.Ordinal))
                is { Document: XmlDocument openDoc })
            {
                openDoc.TreeNodes.SchemaSet = new SchemaResolver(Settings, _schemaCache).Resolve(openDoc);
                _validation.Revalidate();
            }
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
        DockPanel.SetDock(_xpathBar, Dock.Top);
        DockPanel.SetDock(_breadcrumb, Dock.Top);
        rightColumn.Children.Add(_findBar);
        rightColumn.Children.Add(_xpathBar);
        rightColumn.Children.Add(_breadcrumb);
        // Plan 28 #4: outline view docks right of the central pane (200dip). LastChildFill
        // means the central pane fills the remaining space.
        _outlineView.Width = 220;
        DockPanel.SetDock(_outlineView, Dock.Right);
        rightColumn.Children.Add(_outlineView);
        _referencesView.Width = 220;
        DockPanel.SetDock(_referencesView, Dock.Right);
        rightColumn.Children.Add(_referencesView);
        _centralPane = new ContentControl { Content = _nodeList };
        rightColumn.Children.Add(_centralPane);

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
        _problemsView = problems;
        var problemsContainer = new DockPanel { Height = 120 };
        DockPanel.SetDock(problemsContainer, Dock.Bottom);
        problemsContainer.Children.Add(problems);

        // Wire validation: track the active document and push diagnostics to the view
        // and to the DiagnosticIndex so tree rows can show inline error glyphs.
        _state.ActiveDocumentChanged += doc => _validation.SetActiveDocument(doc as XmlDocument);
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
                if (loc.NodeId is { } nodeId)
                {
                    // XML diagnostic — expand ancestors + select the offending node.
                    _nodeList.EnsureVisibleAndSelect(nodeId.ToString());
                    return;
                }
                if (loc.Uri is { } uri)
                {
                    // Text-doc diagnostic (LSP). Plan 27 #2: switch tabs + move caret.
                    var tab = _state.Tabs.FirstOrDefault(t =>
                        string.Equals(t.Document.Source?.AbsoluteUri, uri, StringComparison.Ordinal));
                    if (tab is not null)
                    {
                        _state.ActiveTab = tab;
                        // TextEditorView.MoveCaretTo wired in a later task; for now scroll-only is OK.
                    }
                    return;
                }
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
            Command = new RelayCommand(() =>
            {
                // Plan 27 #7: text docs route to AvaloniaEdit's internal undo stack.
                if (_state.ActiveDocument is Phoenixml.Platform.Editor.Text.TextDocument)
                    _textEditor.Undo();
                else
                    UndoCommand.Execute(this);
            }),
        });
        _window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Y, KeyModifiers.Control),
            Command = new RelayCommand(() =>
            {
                if (_state.ActiveDocument is Phoenixml.Platform.Editor.Text.TextDocument)
                    _textEditor.Redo();
                else
                    RedoCommand.Execute(this);
            }),
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
            Gesture = new KeyGesture(Key.G, KeyModifiers.Control),
            Command = new RelayCommand(ShowXPathBar),
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
                        if (t.Document is XmlDocument xdoc
                            && xdoc.Source is Uri src && System.IO.File.Exists(src.LocalPath))
                        {
                            try
                            {
                                var fresh = await Phoenixml.Platform.Editor.Xml.XmlDocument
                                    .LoadAsync(src.LocalPath, CancellationToken.None);
                                _state.ReplaceDocument(xdoc, fresh);
                            }
                            catch (System.IO.IOException) { t.Document.MarkClean(); }
                            catch (System.Xml.XmlException) { t.Document.MarkClean(); }
                            catch (UnauthorizedAccessException) { t.Document.MarkClean(); }
                        }
                        else
                        {
                            // Never-saved doc / text doc — MarkClean is the only option for MVP.
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

        // Plan 24: persist XPath history on close. Runs alongside the dirty-docs prompt;
        // both handlers fire on Closing.
        _window.Closing += (_, _) =>
        {
            try
            {
                Find.XPathHistoryStore.Save(
                    Find.XPathHistoryStore.DefaultPath,
                    _xpathController.SnapshotHistory());
            }
            catch (IOException) { /* silent */ }
            catch (UnauthorizedAccessException) { /* silent */ }
        };

        _state.ActiveDocumentChanged += OnActiveDocumentChanged;
        _state.ActiveDocumentChanged += SwapCentralPane;

        // Plan 26 #8: per-tab LSP sessions for text documents.
        _state.TabAdded += tab =>
        {
            if (tab.Document is Phoenixml.Platform.Editor.Text.TextDocument text)
                _ = StartLspSessionAsync(tab, text);
        };
        _state.TabClosed += tab =>
        {
            if (_lspSessions.Remove(tab, out var session))
                _ = session.DisposeAsync().AsTask();
        };
    }

    private async Task StartLspSessionAsync(ViewModels.DocumentTab tab, Phoenixml.Platform.Editor.Text.TextDocument doc)
    {
        var path = _lspRegistry.PathFor(doc.Kind);
        if (path is null || !System.IO.File.Exists(path))
        {
            NotifyAction($"LSP server not found for {doc.Kind} ({path ?? "no path"})");
            return;
        }
        try
        {
            var process = new Lsp.LspServerProcess(path);
            var session = Lsp.LspSession.FromProcess(doc, process);
            session.DiagnosticsArrived += OnLspDiagnostics;
            session.RawDiagnosticsArrived += OnLspRawDiagnostics;
            // Plan 28 #9: auto-restart with exponential backoff (1s, 3s, 9s) up to 3 attempts.
            // _lspRestartAttempts[tab] resets on successful didOpen via OnLspRawDiagnostics.
            process.Exited += (_, _) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    var attempts = _lspRestartAttempts.GetValueOrDefault(tab, 0);
                    if (attempts >= 3)
                    {
                        NotifyAction($"LSP server ({doc.Kind}) failed 3 restart attempts — close + reopen tab to retry");
                        return;
                    }
                    attempts++;
                    _lspRestartAttempts[tab] = attempts;
                    var delay = TimeSpan.FromSeconds(Math.Pow(3, attempts - 1));
                    NotifyAction($"LSP ({doc.Kind}) exited — restart {attempts}/3 in {delay.TotalSeconds:0}s");
                    await Task.Delay(delay);

                    if (_lspSessions.TryGetValue(tab, out var staleSession))
                    {
                        await staleSession.DisposeAsync();
                        _lspSessions.Remove(tab);
                    }
                    await StartLspSessionAsync(tab, doc);
                });
            };
            _lspSessions[tab] = session;
            await session.StartAsync();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            NotifyAction($"LSP server failed to start: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            NotifyAction($"LSP server failed to start: {ex.Message}");
        }
    }

    private void OnLspDiagnostics(object? sender, IReadOnlyList<Lsp.EditorLspDiagnostic> diags)
    {
        // Status-segment summary alongside the ProblemsView routing. Keep this terse
        // so the user notices state changes; richer per-message rendering lives in Problems.
        if (diags.Count == 0)
        {
            NotifyAction("LSP: clean");
            return;
        }
        NotifyAction($"LSP: {diags.Count} diagnostic(s)");
    }

    private void OnLspRawDiagnostics(object? sender, Lsp.LspPublishDiagnosticsParams p)
    {
        // Plan 27 #2: feed the raw LSP payload through the bridge → ProblemsView.
        var converted = Lsp.LspProblemsBridge.Convert(p);
        _diagIndex.Reset(converted);
        _problemsView?.SetDiagnostics(converted);

        // Plan 28 #4: refresh outline view after every server publish (cheap RPC).
        if (sender is Lsp.LspSession session) _ = RefreshOutlineAsync(session);

        // Plan 28 #9: a successful publish means the server initialized fine — reset
        // restart attempts for the matching tab so a future crash gets the full retry budget.
        if (sender is Lsp.LspSession s)
        {
            foreach (var pair in _lspSessions)
            {
                if (ReferenceEquals(pair.Value, s))
                {
                    _lspRestartAttempts.Remove(pair.Key);
                    break;
                }
            }
        }
    }

    private async Task RefreshOutlineAsync(Lsp.LspSession session)
    {
        try
        {
            var symbols = await session.RequestDocumentSymbolsAsync();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _outlineView.SetSymbols(symbols));
        }
        catch (InvalidOperationException) { /* session disposed mid-flight */ }
    }

    /// <summary>
    /// Swaps the central pane's content based on the active document's kind.
    /// XML docs get <see cref="NodeListView"/>; text docs get <c>TextEditorView</c>;
    /// XML-only UI (find bar, XPath bar, breadcrumb) is visibility-gated.
    /// </summary>
    private void SwapCentralPane(Phoenixml.Platform.Documents.Document? doc)
    {
        switch (doc)
        {
            case XmlDocument:
                _centralPane.Content = _nodeList;
                _breadcrumb.IsVisible = true;
                _outlineView.IsVisible = false;
                _referencesView.IsVisible = false;
                // find/xpath bars retain their own toggled visibility state
                break;
            case Phoenixml.Platform.Editor.Text.TextDocument text:
                _centralPane.Content = _textEditor;
                // Hand the matching tab's LSP session to the view so popups can call it.
                Lsp.LspSession? session = null;
                foreach (var pair in _lspSessions)
                {
                    if (ReferenceEquals(pair.Key.Document, text)) { session = pair.Value; break; }
                }
                _textEditor.Bind(text, session);
                _findBar.IsVisible = false;
                _xpathBar.IsVisible = false;
                _breadcrumb.IsVisible = false;
                _outlineView.IsVisible = true;
                _referencesView.IsVisible = false;
                // Plan 28 #4: refresh outline. Initial fetch + on every Changed (debounced).
                if (session is not null)
                    _ = RefreshOutlineAsync(session);
                break;
            default:
                _centralPane.Content = _nodeList;
                break;
        }
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
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            Phoenixml.Platform.Documents.Document doc;
            if (ext is ".xq" or ".xqy" or ".xquery" or ".xqm" or ".xsl" or ".xslt")
            {
                doc = await Phoenixml.Platform.Editor.Text.TextDocument
                    .LoadAsync(path, CancellationToken.None);
            }
            else
            {
                doc = await Phoenixml.Platform.Editor.Xml.XmlDocument
                    .LoadAsync(path, CancellationToken.None);
            }
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

        // XPath bar's scalar result takes precedence over the selected-node XPath —
        // both reuse the same segment slot. Falls back to selected-node XPath when the
        // XPath bar is closed or returned nodes (not atomics).
        var xpathLabel = !string.IsNullOrEmpty(_xpathScalar) ? _xpathScalar : _selectedXPath;
        if (!string.IsNullOrEmpty(xpathLabel))
        {
            segments.Add(new StatusBarSegment(
                Id: "selected-xpath",
                Label: xpathLabel,
                Order: 2));
        }

        if (_lastAction.Message is { } actionMsg)
        {
            segments.Add(new StatusBarSegment(
                Id: "last-action",
                Label: actionMsg,
                Order: 3));
        }

        _window.SetStatusSegments(segments, EmptyServiceProvider.Instance);
    }

    /// <summary>Surfaces a transient message in the status bar's last-action segment.</summary>
    internal void NotifyAction(string message) => _lastAction.Notify(message);

    /// <summary>
    /// Prompts the user to pick a second XML file, parses it, computes the structural diff
    /// against the active document, and displays the result in a modal report window.
    /// Dual-pane side-by-side view deferred to Plan 24.
    /// </summary>
    internal async Task CompareWithFileAsync()
    {
        if (_state.ActiveDocument is not XmlDocument left)
        {
            NotifyAction("Compare: no active document");
            return;
        }

        var topLevel = TopLevel.GetTopLevel(_window);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Compare with…",
                AllowMultiple = false,
            });
        if (files.Count == 0) return;

        XmlDocument right;
        try
        {
            right = await XmlDocument.LoadAsync(files[0].Path.LocalPath, CancellationToken.None);
        }
        catch (System.Xml.XmlException ex)
        {
            NotifyAction($"Compare failed: {ex.Message}");
            return;
        }
        catch (System.IO.IOException ex)
        {
            NotifyAction($"Compare failed: {ex.Message}");
            return;
        }

        var window = new Compare.CompareDocsWindow();
        window.Load(left, right);
        window.Show(_window);
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

    private void OnActiveDocumentChanged(Phoenixml.Platform.Documents.Document? doc)
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

        // XML-specific wiring: tree provider + diagnostic/match indexes + schema set.
        // Non-XML docs (TextDocument) skip this branch; their view is wired in SwapCentralPane.
        if (doc is XmlDocument xdoc)
        {
            xdoc.TreeNodes.DiagnosticIndex = _diagIndex;
            xdoc.TreeNodes.MatchIndex = _matchIndex;
            xdoc.TreeNodes.SchemaSet = new SchemaResolver(Settings, _schemaCache).Resolve(xdoc);
            _nodeList.SetProvider(xdoc.TreeNodes);
        }
        else
        {
            _nodeList.SetProvider(null);
        }

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
        // Mutex with XPath bar — both feed the same MatchIndex, so only one tints at a time.
        if (_xpathBar.IsVisible) _xpathController.OnClose();
        _findBar.IsVisible = true;
        _findBar.FocusQueryBox();
    }

    /// <summary>
    /// Shows the find bar in Replace mode (expands the Replace row and focuses the Replace
    /// TextBox). Triggered by Ctrl+H. Standard text-editor binding.
    /// </summary>
    internal void ShowFindBarInReplaceMode()
    {
        if (_xpathBar.IsVisible) _xpathController.OnClose();
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

    /// <summary>
    /// Shows the XPath query bar and focuses its input. Hides the FindBar first when open —
    /// the two highlight modes share <see cref="MatchIndex"/> and would clash if visible
    /// simultaneously. Triggered by Ctrl+G.
    /// </summary>
    internal void ShowXPathBar()
    {
        if (_findBar.IsVisible)
        {
            _findController.OnClose();    // clear FindBar matches before swap
        }
        _xpathBar.IsVisible = true;
        _xpathBar.FocusQueryBox();
    }

    /// <summary>
    /// Hides the XPath bar and resets its state. Called by
    /// <see cref="XPathQueryController.OnClose"/> via <see cref="IXPathQueryHost"/>.
    /// </summary>
    internal void HideXPathBar()
    {
        _xpathBar.IsVisible = false;
        _xpathBar.Reset();
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

        switch (xmlNode.XdmNode)
        {
            case XdmElement el:
                {
                    var xml = SubtreeSerializer.Serialize(doc, el.Id);
                    await _clipboard.WriteXmlAsync(xml);
                    NotifyAction($"Copied element <{el.LocalName}>");
                    return;
                }
            case XdmAttribute attr:
                {
                    var text = AttributeClipboardFormat.Serialize(attr.LocalName, attr.Value);
                    await _clipboard.WriteTextAsync(text);
                    NotifyAction($"Copied attribute @{attr.LocalName}");
                    return;
                }
        }
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

        switch (xmlNode.XdmNode)
        {
            case XdmElement el:
                {
                    var xml = SubtreeSerializer.Serialize(doc, el.Id);
                    await _clipboard.WriteXmlAsync(xml);
                    try
                    {
                        doc.Undo.Execute(new DeleteNodeCommand(doc, el.Id));
                        NotifyAction($"Cut element <{el.LocalName}>");
                    }
                    catch (InvalidOperationException ex)
                    {
                        NotifyAction($"Cut failed: {ex.Message}");
                    }
                    return;
                }
            case XdmAttribute attr:
                {
                    var text = AttributeClipboardFormat.Serialize(attr.LocalName, attr.Value);
                    await _clipboard.WriteTextAsync(text);
                    try
                    {
                        doc.Undo.Execute(new DeleteNodeCommand(doc, attr.Id));
                        NotifyAction($"Cut attribute @{attr.LocalName}");
                    }
                    catch (InvalidOperationException ex)
                    {
                        NotifyAction($"Cut failed: {ex.Message}");
                    }
                    return;
                }
        }
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

        // Try attribute-format text first — pastes attribute onto the selected element.
        var text = await _clipboard.ReadTextAsync();
        if (!string.IsNullOrEmpty(text)
            && AttributeClipboardFormat.TryParse(text, out var attrName, out var attrValue))
        {
            if (xmlNode.XdmNode is not XdmElement el)
            {
                NotifyAction("Paste attribute: select an element first");
                return;
            }
            try
            {
                doc.Undo.Execute(new InsertAttributeCommand(doc, el.Id, attrName, attrValue));
                NotifyAction($"Pasted attribute @{attrName}");
            }
            catch (InvalidOperationException ex)
            {
                NotifyAction($"Paste failed: {ex.Message}");
            }
            return;
        }

        // Fall back to XML subtree paste.
        if (xmlNode.XdmNode is not XdmElement parent)
        {
            NotifyAction("Paste subtree: select an element first");
            return;
        }
        var xml = await _clipboard.ReadXmlAsync();
        if (string.IsNullOrEmpty(xml)) return;

        try
        {
            doc.Undo.Execute(new InsertSubtreeCommand(doc, parent.Id, xml));
            _nodeList.ExpandById(xmlNode.Id);
            NotifyAction("Pasted subtree");
        }
        catch (System.Xml.XmlException ex)
        {
            NotifyAction($"Paste failed: malformed XML ({ex.Message})");
        }
        catch (InvalidOperationException ex)
        {
            NotifyAction($"Paste failed: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // IFindHost explicit implementation
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    XmlDocument? IFindHost.ActiveDocument => _state.ActiveDocument as XmlDocument;

    /// <inheritdoc/>
    XmlTreeNodeProvider? IFindHost.ActiveProvider =>
        (_state.ActiveDocument as XmlDocument)?.TreeNodes as XmlTreeNodeProvider;

    /// <inheritdoc/>
    void IFindHost.EnsureVisibleAndSelect(string nodeId) =>
        _nodeList.EnsureVisibleAndSelect(nodeId);

    /// <inheritdoc/>
    void IFindHost.HideFindBar() => HideFindBar();

    /// <inheritdoc/>
    void IFindHost.SetMatchCount(int active, int total) =>
        _findBar.SetMatchCount(active, total);

    // -------------------------------------------------------------------------
    // IXPathQueryHost explicit implementation
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    XmlDocument? IXPathQueryHost.ActiveDocument => _state.ActiveDocument as XmlDocument;

    /// <inheritdoc/>
    XmlTreeNodeProvider? IXPathQueryHost.ActiveProvider =>
        (_state.ActiveDocument as XmlDocument)?.TreeNodes as XmlTreeNodeProvider;

    /// <inheritdoc/>
    void IXPathQueryHost.EnsureVisibleAndSelect(string nodeId) =>
        _nodeList.EnsureVisibleAndSelect(nodeId);

    /// <inheritdoc/>
    void IXPathQueryHost.HideXPathBar() => HideXPathBar();

    /// <inheritdoc/>
    void IXPathQueryHost.SetXPathMatchCount(int active, int total, string? error) =>
        _xpathBar.SetMatchCount(active, total, error);

    /// <inheritdoc/>
    void IXPathQueryHost.SetXPathScalar(string? scalar)
    {
        _xpathScalar = scalar ?? string.Empty;
        UpdateStatusBar();
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
