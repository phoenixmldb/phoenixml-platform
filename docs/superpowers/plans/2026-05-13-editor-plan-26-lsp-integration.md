# Editor v0.16 — Plan 26 LSP Integration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn Plan 25's tested-but-isolated LSP infrastructure into a working editor feature. Opening a `.xq` or `.xsl` file in the editor should: (1) load it as a `TextDocument`, (2) render an AvaloniaEdit-based text view in place of the XML tree, (3) spawn the corresponding LSP server, (4) wire text edits → `didChange` notifications, and (5) surface server diagnostics in the existing Problems pane with click-to-navigate.

**Architecture:**
- **`EditorState` generalization** — promote `XmlDocument` references to `Document` (the platform base class). `DocumentTab.Document` becomes `Document`; `ActiveDocumentChanged` fires `Document?`. XML-specific consumers (`SchemaResolver`, find/XPath bar, breadcrumb) now type-check the active doc and degrade gracefully (hide / no-op) for non-XML docs.
- **Central pane swap** — the right column's tree-list slot becomes a `ContentControl`. On `ActiveDocumentChanged` the host picks `NodeListView` (XML) or `TextEditorView` (text). Find bar / XPath bar / breadcrumb dock above; their visibility is now doc-kind-aware.
- **`TextEditorView`** — Avalonia `UserControl` hosting `AvaloniaEdit.TextEditor`. Two-way bound to `TextDocument`: typing fires `TextDocument.ReplaceAll(newText)`; external `TextDocument.Changed` (e.g. revert) replaces the editor's buffer. Versioning piggybacks on `TextDocument.Version`.
- **`LspServerProcess`** — wraps `Process.Start` with stdio piping. Disposable; kills the child on dispose. Path resolution is by convention: `tools/phoenixmldb-xquery-lsp` / `tools/phoenixmldb-xslt-lsp` relative to the editor's app directory; configurable via `EditorSettings.LspServerPaths`.
- **`LspSession`** — couples a `TextDocument` with an `LspClient` backed by an `LspServerProcess`. On `TextDocument.Changed` sends `didChange`; on `LspClient.DiagnosticsReceived` routes to `LspDiagnosticBridge` which feeds the editor's `DiagnosticIndex` + `ProblemsView`.
- **`LspDiagnosticBridge`** — translates LSP `Diagnostic` records into the editor's existing `Diagnostic` shape (used by `ProblemsView`). Two flavours of `Location`: `DiagnosticLocation` (NodeId) for XML, new `TextLocation` (Line + Column) for text docs. `ProblemsView` already groups by file; click-to-navigate routes via document kind.

**Tech Stack:** Avalonia 11.2.4, AvaloniaEdit 11.2.0 (CPM-pinned in Plan 25, now referenced), StreamJsonRpc, PhoenixmlDb.Core/XQuery/Xslt/Xslt.LanguageServer/XQuery.LanguageServer.

**Out of scope (Plan 27+):**
- LSP hover popup in text view (server already publishes hover; client wiring deferred)
- Completion popup in text view
- LSP-driven semantic tokens / syntax highlighting (AvaloniaEdit default highlight only)
- Find / Replace in text view
- Undo/redo of text edits beyond AvaloniaEdit's built-in
- Multi-server orchestration when one file imports another
- Server crash recovery / restart

---

## File structure

| File | Role |
|---|---|
| `src/Phoenixml.Editor/ViewModels/DocumentTab.cs` | Modify — generalize from `XmlDocument` to `Document` |
| `src/Phoenixml.Editor/ViewModels/EditorState.cs` | Modify — generalize ActiveDocument/OpenDocument to `Document` |
| `src/Phoenixml.Editor/EditorMainWindow.cs` | Modify — central pane swap, doc-kind-aware UI, LSP wiring |
| `src/Phoenixml.Editor/Views/TextEditorView.axaml(.cs)` | NEW — AvaloniaEdit host bound to `TextDocument` |
| `src/Phoenixml.Editor/Lsp/LspServerProcess.cs` | NEW — process spawn + dispose |
| `src/Phoenixml.Editor/Lsp/LspSession.cs` | NEW — TextDocument ↔ LspClient ↔ diagnostics |
| `src/Phoenixml.Editor/Lsp/LspDiagnosticBridge.cs` | NEW — LSP Diagnostic → editor Diagnostic |
| `src/Phoenixml.Editor/Lsp/LspServerRegistry.cs` | NEW — DocumentKind → server-binary path mapping |
| `src/Phoenixml.Editor/Settings/EditorSettings.cs` | Modify — `LspServerPaths` dictionary (optional override) |
| `src/Phoenixml.Editor/Commands/OpenFileCommand.cs` | Modify — route by extension to `XmlDocument` or `TextDocument` |
| `src/Phoenixml.Platform/Abstractions/Diagnostics/Diagnostic.cs` (or wherever Diagnostic lives) | Modify — add `TextLocation` discriminator |
| `test/Phoenixml.Editor.Tests/Lsp/LspSessionTests.cs` | NEW — 4 session tests (in-memory streams) |
| `test/Phoenixml.Editor.Tests/Lsp/LspDiagnosticBridgeTests.cs` | NEW — 3 conversion tests |
| `test/Phoenixml.Editor.Tests/Lsp/LspServerRegistryTests.cs` | NEW — 3 path-resolution tests |
| `docs/editor-v0.16-walkthrough.md` | NEW |

---

## Tasks

### Task 1: Generalize `EditorState` + `DocumentTab` from `XmlDocument` to `Document`

**Files:**
- Modify: `src/Phoenixml.Editor/ViewModels/DocumentTab.cs`
- Modify: `src/Phoenixml.Editor/ViewModels/EditorState.cs`
- Modify: every caller of `_state.ActiveDocument` / `_state.OpenDocument` / `_state.Tabs.X.Document`

**Approach:** Replace `XmlDocument` with `Document` (the platform base class — `Phoenixml.Platform.Documents.Document`). Add a `TryGetXml(out XmlDocument? xml)` helper on `DocumentTab` so XML-specific code reads:

```csharp
if (tab.TryGetXml(out var xml)) { ... XML stuff ... }
```

Existing callers do `_state.ActiveDocument is XmlDocument xdoc` already in places — those keep working. Other callers need updates.

- [ ] **Step 1: Update `DocumentTab`**

```csharp
internal sealed class DocumentTab : INotifyPropertyChanged
{
    public DocumentTab(Document document) { Document = document; ... }
    public Document Document { get; }
    public bool TryGetXml(out XmlDocument? xml)
    {
        xml = Document as XmlDocument;
        return xml is not null;
    }
}
```

- [ ] **Step 2: Update `EditorState`**

Replace all `XmlDocument?` with `Document?` (on `ActiveDocument`, `ActiveDocumentChanged`, `OpenDocument`, `ReplaceDocument`). Add a parallel `OpenDocument(Document)` overload — keep the `XmlDocument` one as a thin call-through for backwards source compatibility.

- [ ] **Step 3: Build platform-wide; fix compile errors in callers**

Run: `dotnet build src/Phoenixml.Editor/Phoenixml.Editor.csproj --nologo 2>&1 | grep -E "CS\d{4}" | head -30`

Each error site needs a `Document` → `XmlDocument` pattern match. The pattern is:

```csharp
// Before:
if (_state.ActiveDocument is null) return;
DoXmlStuff(_state.ActiveDocument);

// After:
if (_state.ActiveDocument is not XmlDocument xdoc) return;
DoXmlStuff(xdoc);
```

- [ ] **Step 4: Run tests — expect green**

Run: `dotnet test --nologo 2>&1 | tail -8`
Expected: all suites pass.

---

### Task 2: Wire `AvaloniaEdit` reference and build `TextEditorView`

**Files:**
- Modify: `src/Phoenixml.Editor/Phoenixml.Editor.csproj` (add `<PackageReference Include="Avalonia.AvaloniaEdit" />`)
- Create: `src/Phoenixml.Editor/Views/TextEditorView.axaml`
- Create: `src/Phoenixml.Editor/Views/TextEditorView.axaml.cs`

- [ ] **Step 1: Reference AvaloniaEdit (CPM pin already set in Plan 25)**

```xml
<PackageReference Include="Avalonia.AvaloniaEdit" />
```

- [ ] **Step 2: AXAML**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ae="https://github.com/avaloniaui/avaloniaedit"
             x:Class="Phoenixml.Editor.Views.TextEditorView">
    <ae:TextEditor Name="Editor"
                   FontFamily="Cascadia Mono, Consolas, monospace"
                   FontSize="13"
                   ShowLineNumbers="True"
                   Background="#1E1E1E"
                   Foreground="#D4D4D4" />
</UserControl>
```

- [ ] **Step 3: Code-behind**

```csharp
public sealed partial class TextEditorView : UserControl
{
    private TextDocument? _document;
    private bool _suppress;

    public TextEditorView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public void Bind(TextDocument document)
    {
        var editor = this.FindControl<TextEditor>("Editor")!;
        if (_document is not null) _document.Changed -= OnDocumentChanged;
        _document = document;
        _suppress = true;
        editor.Document.Text = document.Text;
        _suppress = false;
        editor.Document.TextChanged += OnEditorTextChanged;
        document.Changed += OnDocumentChanged;
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppress || _document is null) return;
        var editor = this.FindControl<TextEditor>("Editor")!;
        _document.ReplaceAll(editor.Document.Text);
    }

    private void OnDocumentChanged(DocumentChange change)
    {
        var editor = this.FindControl<TextEditor>("Editor")!;
        if (editor.Document.Text == _document!.Text) return; // we sent it
        _suppress = true;
        try { editor.Document.Text = _document.Text; }
        finally { _suppress = false; }
    }
}
```

(The `_suppress` flag prevents feedback loops where setting `editor.Document.Text` fires `TextChanged` which would push back into `TextDocument.ReplaceAll` and re-fire `Document.Changed`.)

- [ ] **Step 4: Build**

Run: `dotnet build src/Phoenixml.Editor/Phoenixml.Editor.csproj --nologo 2>&1 | tail -5`
Expected: 0 errors.

---

### Task 3: Central pane swap on doc-kind change

**Files:**
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs`

**Approach:** Replace the direct `rightColumn.Children.Add(_nodeList)` with a `ContentControl` that swaps between `_nodeList` and `_textEditor`. Same for hiding `_findBar`/`_xpathBar`/`_breadcrumb` when the active doc is text.

- [ ] **Step 1: Add the text editor field + content host**

```csharp
private readonly TextEditorView _textEditor = new();
private ContentControl _centralPane = null!;
```

- [ ] **Step 2: Build the content host in the central column**

```csharp
_centralPane = new ContentControl { Content = _nodeList };
rightColumn.Children.Add(_centralPane);
```

(Replace `rightColumn.Children.Add(_nodeList)` with this.)

- [ ] **Step 3: Hook `ActiveDocumentChanged` to swap content**

In the constructor after `_state.ActiveDocumentChanged += OnActiveDocumentChanged`:

```csharp
_state.ActiveDocumentChanged += SwapCentralPane;

private void SwapCentralPane(Document? doc)
{
    switch (doc)
    {
        case XmlDocument xml:
            _centralPane.Content = _nodeList;
            _findBar.IsVisible = _findBar.IsVisible; // preserve toggle state
            _breadcrumb.IsVisible = true;
            break;
        case TextDocument text:
            _centralPane.Content = _textEditor;
            _textEditor.Bind(text);
            _findBar.IsVisible = false;
            _xpathBar.IsVisible = false;
            _breadcrumb.IsVisible = false;
            break;
        default:
            _centralPane.Content = _nodeList;
            break;
    }
}
```

- [ ] **Step 4: Build + smoke**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: 0 errors. Manual smoke happens after Task 4.

---

### Task 4: Route `.xq`/`.xsl` files to `TextDocument` in OpenFileCommand

**Files:**
- Modify: `src/Phoenixml.Editor/Commands/OpenFileCommand.cs`

- [ ] **Step 1: Branch on extension**

Find the existing `LoadAsync(path)` call. Replace with:

```csharp
var ext = Path.GetExtension(path).ToLowerInvariant();
Document doc;
if (ext is ".xq" or ".xqy" or ".xquery" or ".xqm" or ".xsl" or ".xslt")
{
    doc = await TextDocument.LoadAsync(path, ct);
}
else
{
    doc = await XmlDocument.LoadAsync(path, ct);
}
window.State.OpenDocument(doc);
```

(`EditorState.OpenDocument(Document)` is the Task 1 overload.)

- [ ] **Step 2: Smoke**

Run the editor; open an `.xq` file. The text editor should open, no XML error. Edits should persist (typing changes the `TextDocument.Text`).

```bash
dotnet run --project src/Phoenixml.Editor --configuration Release -- /tmp/test.xq
```

(Create a tiny `/tmp/test.xq` first: `echo '1 + 1' > /tmp/test.xq`.)

---

### Task 5: `LspServerProcess` + `LspServerRegistry`

**Files:**
- Create: `src/Phoenixml.Editor/Lsp/LspServerProcess.cs`
- Create: `src/Phoenixml.Editor/Lsp/LspServerRegistry.cs`
- Create: `test/Phoenixml.Editor.Tests/Lsp/LspServerRegistryTests.cs`

- [ ] **Step 1: Write `LspServerRegistry` tests first**

```csharp
public class LspServerRegistryTests
{
    [Fact]
    public void ResolvesXQueryBinaryByDocumentKind()
    {
        var reg = new LspServerRegistry(rootDir: "/opt/phoenixml");
        Assert.Equal("/opt/phoenixml/tools/phoenixmldb-xquery-lsp", reg.PathFor(DocumentKind.XQuery));
    }

    [Fact]
    public void ResolvesXsltBinaryByDocumentKind()
    {
        var reg = new LspServerRegistry(rootDir: "/opt/phoenixml");
        Assert.Equal("/opt/phoenixml/tools/phoenixmldb-xslt-lsp", reg.PathFor(DocumentKind.Xslt));
    }

    [Fact]
    public void ReturnsNullForUnsupportedKind()
    {
        var reg = new LspServerRegistry(rootDir: "/opt/phoenixml");
        Assert.Null(reg.PathFor(DocumentKind.Xml));
    }
}
```

- [ ] **Step 2: Implement `LspServerRegistry`**

```csharp
public sealed class LspServerRegistry
{
    private readonly string _rootDir;
    public LspServerRegistry(string rootDir) => _rootDir = rootDir;

    public string? PathFor(DocumentKind kind) => kind switch
    {
        DocumentKind.XQuery => Path.Combine(_rootDir, "tools", "phoenixmldb-xquery-lsp"),
        DocumentKind.Xslt => Path.Combine(_rootDir, "tools", "phoenixmldb-xslt-lsp"),
        _ => null,
    };
}
```

- [ ] **Step 3: Implement `LspServerProcess`**

```csharp
public sealed class LspServerProcess : IDisposable
{
    private readonly Process _proc;

    public LspServerProcess(string exePath)
    {
        _proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };
        _proc.Start();
    }

    public Stream StandardInput => _proc.StandardInput.BaseStream;
    public Stream StandardOutput => _proc.StandardOutput.BaseStream;

    public void Dispose()
    {
        try
        {
            if (!_proc.HasExited)
            {
                _proc.Kill(entireProcessTree: true);
                _proc.WaitForExit(2000);
            }
        }
        catch { /* best effort */ }
        _proc.Dispose();
    }
}
```

- [ ] **Step 4: Run registry tests**

Run: `dotnet test test/Phoenixml.Editor.Tests --filter LspServerRegistryTests --nologo 2>&1 | tail -3`
Expected: 3 / 3 passing.

(No unit tests for `LspServerProcess` — it's a thin wrapper that requires a real binary.)

---

### Task 6: `LspDiagnosticBridge` — LSP diagnostic → editor diagnostic

**Files:**
- Modify: `src/Phoenixml.Platform/Abstractions/Diagnostics/` (find `Diagnostic` + `DiagnosticLocation` — likely path)
- Create: `src/Phoenixml.Editor/Lsp/LspDiagnosticBridge.cs`
- Create: `test/Phoenixml.Editor.Tests/Lsp/LspDiagnosticBridgeTests.cs`

- [ ] **Step 1: Audit existing Diagnostic shape**

Run: `grep -rn "class Diagnostic\|record Diagnostic\|DiagnosticLocation\|DiagnosticSeverity" src/Phoenixml.Platform/ src/Phoenixml.Platform.Editor.Xml/ --include="*.cs" | grep -v obj/ | head -10`

The editor's `Diagnostic` currently carries a `DiagnosticLocation` with a `NodeId`. For text-doc diagnostics we need a `TextLocation(int Line, int Column, int Length)` discriminator.

- [ ] **Step 2: Add `TextLocation` alongside `DiagnosticLocation`**

```csharp
// Existing:
public sealed record DiagnosticLocation(NodeId NodeId);

// New:
public sealed record TextLocation(string Uri, int Line, int Column, int Length);

// Diagnostic gains nullable TextLocation:
public sealed record Diagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    DiagnosticLocation? Location,
    TextLocation? TextLocation = null);
```

- [ ] **Step 3: Implement the bridge**

```csharp
public static class LspDiagnosticBridge
{
    public static IReadOnlyList<Diagnostic> Convert(LspPublishDiagnosticsParams p) =>
        p.Diagnostics.Select(d => new Diagnostic(
            Code: d.Code ?? "",
            Severity: d.Severity switch { 1 => DiagnosticSeverity.Error, 2 => DiagnosticSeverity.Warning, _ => DiagnosticSeverity.Info },
            Message: d.Message,
            Location: null,
            TextLocation: new TextLocation(p.Uri, d.Range.Start.Line, d.Range.Start.Character,
                d.Range.End.Character - d.Range.Start.Character)
        )).ToArray();
}
```

- [ ] **Step 4: Write 3 conversion tests**

```csharp
// - Empty diagnostics → empty list
// - Severity mapping (1=Error, 2=Warning, 3=Info)
// - TextLocation captures URI + range
```

- [ ] **Step 5: Run tests**

---

### Task 7: `LspSession` — couples `TextDocument` to `LspClient`

**Files:**
- Create: `src/Phoenixml.Editor/Lsp/LspSession.cs`
- Create: `test/Phoenixml.Editor.Tests/Lsp/LspSessionTests.cs`

**Approach:** `LspSession` owns an `LspClient` + `LspServerProcess`. On construction it sends `initialize` + `didOpen`. Subscribes to `TextDocument.Changed` → `didChange`. Subscribes to `LspClient.DiagnosticsReceived` → emits its own `DiagnosticsArrived` event for the editor to consume. `DisposeAsync` sends `didClose` + tears down the client + process.

- [ ] **Step 1: Implement `LspSession`**

```csharp
public sealed class LspSession : IAsyncDisposable
{
    private readonly TextDocument _doc;
    private readonly LspServerProcess _process;
    private readonly LspClient _client;
    private readonly string _languageId;

    public LspSession(TextDocument doc, LspServerProcess process)
    {
        _doc = doc;
        _process = process;
        _client = new LspClient(process.StandardInput, process.StandardOutput);
        _languageId = doc.Kind switch
        {
            DocumentKind.XQuery => "xquery",
            DocumentKind.Xslt => "xsl",
            _ => "plaintext",
        };
        _client.DiagnosticsReceived += OnDiagnostics;
        _doc.Changed += OnDocumentChanged;
    }

    public event EventHandler<IReadOnlyList<Diagnostic>>? DiagnosticsArrived;

    public async Task StartAsync()
    {
        await _client.InitializeAsync();
        await _client.NotifyInitializedAsync();
        await _client.NotifyDidOpenAsync(_doc.Source!.AbsoluteUri, _languageId, _doc.Version, _doc.Text);
    }

    private void OnDocumentChanged(DocumentChange c)
    {
        if (_doc.Source is null) return;
        _ = _client.NotifyDidChangeAsync(_doc.Source.AbsoluteUri, _doc.Version, _doc.Text);
    }

    private void OnDiagnostics(object? sender, LspPublishDiagnosticsParams p) =>
        DiagnosticsArrived?.Invoke(this, LspDiagnosticBridge.Convert(p));

    public async ValueTask DisposeAsync()
    {
        _doc.Changed -= OnDocumentChanged;
        _client.DiagnosticsReceived -= OnDiagnostics;
        await _client.DisposeAsync();
        _process.Dispose();
    }
}
```

- [ ] **Step 2: Write 4 tests using in-memory streams + stub server**

Same pattern as `LspClientTests`. Verify:
- `StartAsync` sends `initialize` + `initialized` + `didOpen`
- `TextDocument.ReplaceAll` triggers `didChange` with the new version
- Server `publishDiagnostics` fires `DiagnosticsArrived` with converted diagnostics
- `DisposeAsync` unsubscribes cleanly (no further events fire after dispose)

- [ ] **Step 3: Run tests**

---

### Task 8: Wire LSP sessions into `EditorMainWindow`

**Files:**
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs`

**Approach:** Maintain `Dictionary<DocumentTab, LspSession> _sessions`. On `OpenDocument` for a text doc, look up `LspServerRegistry.PathFor(doc.Kind)`, spawn `LspServerProcess`, build `LspSession`, hook `DiagnosticsArrived` → existing `DiagnosticIndex` + `ProblemsView`. On tab close, dispose the session.

- [ ] **Step 1: Add the session map + registry**

```csharp
private readonly LspServerRegistry _lspRegistry;
private readonly Dictionary<DocumentTab, LspSession> _lspSessions = new();
```

Initialize `_lspRegistry` in the constructor from the editor's app directory:

```csharp
_lspRegistry = new LspServerRegistry(AppContext.BaseDirectory);
```

- [ ] **Step 2: Hook tab-open / tab-close**

```csharp
_state.TabAdded += tab =>
{
    if (tab.Document is TextDocument text)
        _ = StartLspSessionAsync(tab, text);
};
_state.TabClosed += tab =>
{
    if (_lspSessions.Remove(tab, out var session))
        _ = session.DisposeAsync();
};

private async Task StartLspSessionAsync(DocumentTab tab, TextDocument doc)
{
    var path = _lspRegistry.PathFor(doc.Kind);
    if (path is null || !File.Exists(path))
    {
        NotifyAction($"LSP server not found for {doc.Kind} ({path ?? "no path"})");
        return;
    }
    try
    {
        var process = new LspServerProcess(path);
        var session = new LspSession(doc, process);
        session.DiagnosticsArrived += OnLspDiagnostics;
        _lspSessions[tab] = session;
        await session.StartAsync();
    }
    catch (Exception ex)
    {
        NotifyAction($"LSP server failed to start: {ex.Message}");
    }
}

private void OnLspDiagnostics(object? sender, IReadOnlyList<Diagnostic> diags)
{
    _diagIndex.Reset(diags);   // existing diagnostic surface
    // ProblemsView refreshes via diagIndex subscription
}
```

(`EditorState.TabAdded` / `TabClosed` may need to be added in Task 1 if not present.)

- [ ] **Step 3: Build + smoke**

Run: `dotnet build --nologo`. Run the editor with `/tmp/broken.xq` containing `1 +`. The Problems pane should show a parse error.

---

### Task 9: Walkthrough + commit

**Files:**
- Create: `docs/editor-v0.16-walkthrough.md`

Same template as v0.15: status header, "What's new" section per task, manual smoke checklist, carried items.

- [ ] **Step 1: Write walkthrough**

- [ ] **Step 2: Full test run**

Run: `dotnet test --nologo 2>&1 | tail -8`
Expected: all green. +10 new tests (3 registry, 3 bridge, 4 session).

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
feat(editor): v0.16 LSP integration — wire Plan 25's servers into the editor

Generalizes EditorState/DocumentTab from XmlDocument to Document so the
text vocabulary (TextDocument) can ride the same tab system. Central
pane is now a ContentControl that swaps between NodeListView (XML) and
TextEditorView (text) based on active document kind.

TextEditorView wraps Avalonia.AvaloniaEdit, two-way bound to TextDocument.
A _suppress flag prevents feedback loops between editor.Document.Text
and TextDocument.ReplaceAll.

LspServerRegistry resolves DocumentKind to a server binary path under
AppContext.BaseDirectory/tools. LspServerProcess wraps Process.Start
with stdio piping + kill-on-dispose. LspSession couples TextDocument
to LspClient: initialize + didOpen on Start; didChange on document edit;
publishDiagnostics → DiagnosticsArrived event consumed by the editor's
existing DiagnosticIndex + ProblemsView.

Find bar / XPath bar / breadcrumb are now visibility-gated by active
document kind — XML-only features hide when a .xq/.xsl file is active.

10 new tests (3 LspServerRegistry, 3 LspDiagnosticBridge, 4 LspSession
via in-memory pipes + stub server).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Risk register

- **`EditorState` refactor blast radius** — every consumer of `_state.ActiveDocument` needs an `is XmlDocument` check. ~20-30 call sites. Mostly mechanical.
- **AvaloniaEdit version compatibility** — pinned at 11.2.0 (compat with Avalonia 11.2.4). Surprises in `TextEditor.Document.Text` semantics likely.
- **LSP server binary availability** — `AppContext.BaseDirectory/tools/...` is a convention. If the binary isn't there (which is the case in `dotnet run` without a publish step), LSP silently fails with a status-bar notice. Editor remains usable, just no diagnostics. Manual smoke needs `dotnet publish` of both server projects into `tools/`.
- **Feedback loops** in `TextEditorView` between editor `TextChanged` and `TextDocument.Changed` — the `_suppress` flag is critical and easy to get wrong. Watch for infinite loops in manual smoke.
- **AvaloniaEdit's own undo stack** vs editor's `IUndoStack` — for MVP we let AvaloniaEdit handle undo internally. The editor's undo system doesn't track text edits. Plan 27 candidate.
