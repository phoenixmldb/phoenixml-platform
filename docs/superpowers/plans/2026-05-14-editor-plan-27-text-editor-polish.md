# Editor v0.17 — Plan 27 Text Editor Polish Pack — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn Plan 26's working-but-rough text editor + LSP integration into something that feels like a real editor. Eight polish items: LSP diagnostics in ProblemsView, custom syntax highlighting for XQuery/XSLT, LSP completion + hover popups in the text view, find/replace in text, editor-side undo/redo bridging, LSP server crash recovery, and an MSBuild step to auto-publish server binaries.

**Architecture:**
- **`DiagnosticLocation` refactor** — make `NodeId` nullable so LSP-driven text-position diagnostics can flow through `IDiagnostic` + `ProblemsView`. Add `Uri` to the record so per-tab grouping works.
- **LSP → ProblemsView bridge** — replace `EditorLspDiagnostic` (Plan 26's intermediate) with conversion straight into `IDiagnostic` records keyed by URI. `ProblemsView` already groups by file path.
- **Syntax highlighting** — `Avalonia.AvaloniaEdit.TextMate` is overkill for our needs; instead ship custom `IHighlightingDefinition` instances loaded from embedded `.xshd` resources. XSLT inherits AvaloniaEdit's built-in XML highlight; XQuery gets a hand-curated definition covering keywords, function-call sites, strings, comments, and operators.
- **Hover + completion popups** — AvaloniaEdit raises `TextArea.Caret.PositionChanged` and `TextArea.TextEntered`. We hook those to send `textDocument/hover` and `textDocument/completion` requests, then render popups anchored to the caret. Both popups use the same Avalonia `Popup` + `ListBox` shape as the XPath bar's autocompletion (Plan 24).
- **Find in text** — adapt `FindBar` from XML to also drive text-buffer search via a new `ITextFindHost` parallel to `IFindHost`. Text-buffer search highlights matches via AvaloniaEdit's `TextSegmentCollection` (built-in selection range overlay).
- **Undo/Redo bridging** — `Document.Undo` is XmlDocument-specific (command-based). For TextDocument, expose `UndoCommand` / `RedoCommand` that route Ctrl+Z / Ctrl+Y to AvaloniaEdit's internal undo stack when the central pane is the text editor.
- **Crash recovery** — `LspServerProcess` raises `Exited`; `LspSession` catches it, tears down the client, spawns a fresh process, sends `initialize` + `didOpen` again with the current buffer.
- **MSBuild auto-publish** — editor csproj `BeforeBuild` target that runs `dotnet publish` of both LSP server projects when their outputs are missing or stale.

**Tech Stack:** Avalonia 11.2.4, AvaloniaEdit 11.2.0, StreamJsonRpc, PhoenixmlDb.Core/XQuery/Xslt/LanguageServer projects.

**Out of scope (Plan 28+):**
- LSP definition / references / formatting / code actions / signature help / inlay hints (server work)
- Compare-view depth (carried since Plan 24)
- LSP workspace-folders / multi-root projects
- Semantic-tokens-driven highlighting (we use static `.xshd`)
- Find-all-references across multiple text docs
- Incremental sync (still full-text in MVP)

---

## File structure

| File | Role |
|---|---|
| `src/Phoenixml.Platform/Abstractions/Editing/IDiagnostic.cs` | Modify — `NodeId` becomes nullable; add `Uri`, `Length` |
| `src/Phoenixml.Platform.Editor.Xml/Validation/XmlSchemaValidator.cs` (and other callsites) | Modify — propagate `Uri` when constructing `DiagnosticLocation` |
| `src/Phoenixml.Editor/Lsp/LspProblemsBridge.cs` | NEW — LSP diagnostic → `IDiagnostic` for ProblemsView |
| `src/Phoenixml.Editor/EditorMainWindow.cs` | Modify — route LSP diagnostics through ProblemsView; wire hover/completion/find/undo |
| `src/Phoenixml.Editor/Lsp/LspClient.cs` | Modify — add `RequestHoverAsync`, `RequestCompletionAsync` |
| `src/Phoenixml.Editor/Lsp/LspMessages.cs` | Modify — add Hover/Completion request DTOs |
| `src/Phoenixml.Editor/Views/TextEditorView.axaml(.cs)` | Modify — hover/completion popups; bind to `LspSession` |
| `src/Phoenixml.Editor/Views/SyntaxHighlighting/XQuery.xshd` | NEW — XQuery highlighting definition (embedded resource) |
| `src/Phoenixml.Editor/Views/SyntaxHighlighting/HighlightingRegistry.cs` | NEW — loads + caches `IHighlightingDefinition` by `DocumentKind` |
| `src/Phoenixml.Editor/Find/TextFindController.cs` | NEW — find/replace against `TextDocument.Text` |
| `src/Phoenixml.Editor/Find/ITextFindHost.cs` | NEW — host surface for text-buffer find |
| `src/Phoenixml.Editor/Lsp/LspSession.cs` | Modify — restart on `Process.Exited` |
| `src/Phoenixml.Editor/Phoenixml.Editor.csproj` | Modify — embed `XQuery.xshd`; add `PublishLspServers` `BeforeBuild` target |
| `test/Phoenixml.Editor.Tests/Find/TextFindControllerTests.cs` | NEW — 5 tests |
| `test/Phoenixml.Editor.Tests/Lsp/LspProblemsBridgeTests.cs` | NEW — 4 tests |
| `test/Phoenixml.Editor.Tests/Views/SyntaxHighlighting/HighlightingRegistryTests.cs` | NEW — 3 tests |
| `docs/editor-v0.17-walkthrough.md` | NEW |

---

## Tasks

### Task 1: Refactor `DiagnosticLocation` for text positions

**Files:**
- Modify: `src/Phoenixml.Platform/Abstractions/Editing/IDiagnostic.cs`
- Modify: every `new DiagnosticLocation(...)` call site (validation pipeline)

**Approach:** Make `NodeId` nullable, add `Uri` (for cross-document grouping) and `Length` (for squiggle sizing). XML validator continues to set `NodeId`; LSP bridge sets `Uri` + `Line`/`Column`/`Length`.

- [ ] **Step 1: Update the record**

```csharp
public sealed record DiagnosticLocation(
    NodeId? NodeId,
    int Line,
    int Column,
    int Length = 1,
    string? Uri = null);
```

- [ ] **Step 2: Fix XML validator call sites**

Run: `grep -rn "new DiagnosticLocation(" src/ --include="*.cs" | grep -v obj/`

Each call site adapts to the new positional constructor — existing args become `(nodeId, line, column)`; default `Length=1` and `Uri=null` are fine for XML.

- [ ] **Step 3: Build + test**

Run: `dotnet test --nologo 2>&1 | tail -5`
Expected: green; all XML validator tests still pass.

---

### Task 2: `LspProblemsBridge` → `IDiagnostic` records

**Files:**
- Create: `src/Phoenixml.Editor/Lsp/LspProblemsBridge.cs`
- Create: `test/Phoenixml.Editor.Tests/Lsp/LspProblemsBridgeTests.cs`

- [ ] **Step 1: Write tests**

```csharp
// 4 tests:
// - Empty LSP diagnostics → empty list
// - Severity 1/2/3 maps to Error/Warning/Info
// - DiagnosticLocation carries Uri + (Line+1) + (Column+1) (LSP 0-based → editor 1-based)
// - Code + Source propagate
```

- [ ] **Step 2: Implement**

```csharp
public static class LspProblemsBridge
{
    public static IReadOnlyList<Diagnostic> Convert(LspPublishDiagnosticsParams p) =>
        p.Diagnostics.Select(d => new Diagnostic(
            Code: d.Code ?? "",
            Severity: d.Severity switch { 1 => DiagnosticSeverity.Error, 2 => DiagnosticSeverity.Warning, _ => DiagnosticSeverity.Info },
            Message: d.Message,
            Location: new DiagnosticLocation(
                NodeId: null,
                Line: d.Range.Start.Line + 1,
                Column: d.Range.Start.Character + 1,
                Length: Math.Max(1, d.Range.End.Character - d.Range.Start.Character),
                Uri: p.Uri))).ToArray();
}
```

- [ ] **Step 3: Wire `LspSession.DiagnosticsArrived` through the bridge**

In `EditorMainWindow.OnLspDiagnostics`, replace the `NotifyAction` with:

```csharp
private void OnLspDiagnostics(object? sender, LspPublishDiagnosticsParams p)
{
    var diags = LspProblemsBridge.Convert(p);
    _diagIndex.Reset(diags);
    // ProblemsView refreshes from _diagIndex automatically
}
```

(Need to update `LspSession.DiagnosticsArrived` to expose the raw `LspPublishDiagnosticsParams` instead of the intermediate `EditorLspDiagnostic` records — or keep both and have the bridge accept the intermediate. Simpler: change the signature.)

- [ ] **Step 4: Update ProblemsView click-to-navigate**

The existing handler activates the XML tree node. Extend so when `Location.NodeId is null && Location.Uri is not null`, it switches the central pane's tab to the matching text doc and moves the caret to `(Line, Column)`.

```csharp
problems.DiagnosticActivated += diag =>
{
    if (diag.Location is not { } loc) return;
    if (loc.NodeId is { } nodeId)
    {
        _nodeList.EnsureVisibleAndSelect(nodeId.ToString());
        return;
    }
    if (loc.Uri is { } uri)
    {
        // Find tab by URI, activate, move caret
        var tab = _state.Tabs.FirstOrDefault(t =>
            t.Document.Source?.AbsoluteUri == uri);
        if (tab is not null)
        {
            _state.ActiveTab = tab;
            _textEditor.MoveCaretTo(loc.Line - 1, loc.Column - 1);
        }
    }
};
```

- [ ] **Step 5: Run all tests**

---

### Task 3: Syntax highlighting via embedded `.xshd`

**Files:**
- Create: `src/Phoenixml.Editor/Views/SyntaxHighlighting/XQuery.xshd`
- Create: `src/Phoenixml.Editor/Views/SyntaxHighlighting/HighlightingRegistry.cs`
- Create: `test/Phoenixml.Editor.Tests/Views/SyntaxHighlighting/HighlightingRegistryTests.cs`
- Modify: `src/Phoenixml.Editor/Phoenixml.Editor.csproj` (embed the .xshd)
- Modify: `src/Phoenixml.Editor/Views/TextEditorView.axaml.cs` (apply highlighting in `Bind`)

- [ ] **Step 1: Create `XQuery.xshd`**

```xml
<?xml version="1.0"?>
<SyntaxDefinition name="XQuery"
                  xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
  <Color name="Comment"  foreground="#6A9955" />
  <Color name="String"   foreground="#CE9178" />
  <Color name="Keyword"  foreground="#569CD6" fontWeight="bold" />
  <Color name="Function" foreground="#DCDCAA" />
  <Color name="Number"   foreground="#B5CEA8" />
  <Color name="Operator" foreground="#D4D4D4" />
  <Color name="Variable" foreground="#9CDCFE" />

  <RuleSet>
    <Span color="Comment" multiline="true">
      <Begin>\(:</Begin>
      <End>:\)</End>
    </Span>
    <Span color="String"><Begin>"</Begin><End>"</End></Span>
    <Span color="String"><Begin>'</Begin><End>'</End></Span>

    <Keywords color="Keyword">
      <Word>declare</Word><Word>function</Word><Word>variable</Word>
      <Word>namespace</Word><Word>module</Word><Word>import</Word>
      <Word>for</Word><Word>let</Word><Word>where</Word><Word>order</Word>
      <Word>by</Word><Word>return</Word><Word>if</Word><Word>then</Word>
      <Word>else</Word><Word>typeswitch</Word><Word>case</Word>
      <Word>default</Word><Word>as</Word><Word>at</Word><Word>in</Word>
      <Word>satisfies</Word><Word>some</Word><Word>every</Word>
      <Word>and</Word><Word>or</Word><Word>not</Word><Word>to</Word>
      <Word>div</Word><Word>idiv</Word><Word>mod</Word>
      <Word>union</Word><Word>intersect</Word><Word>except</Word>
      <Word>instance</Word><Word>of</Word><Word>cast</Word><Word>castable</Word>
      <Word>treat</Word><Word>true</Word><Word>false</Word>
    </Keywords>

    <Rule color="Variable">\$[A-Za-z_][A-Za-z0-9_-]*</Rule>
    <Rule color="Number">\b\d+(\.\d+)?\b</Rule>
    <Rule color="Function">[A-Za-z_][A-Za-z0-9_-]*(?=\s*\()</Rule>
    <Rule color="Operator">[+\-*/=<>!]+</Rule>
  </RuleSet>
</SyntaxDefinition>
```

- [ ] **Step 2: Embed in csproj**

```xml
<ItemGroup>
  <EmbeddedResource Include="Views/SyntaxHighlighting/XQuery.xshd" />
</ItemGroup>
```

- [ ] **Step 3: Implement `HighlightingRegistry`**

```csharp
public sealed class HighlightingRegistry
{
    private readonly Dictionary<DocumentKind, IHighlightingDefinition?> _cache = new();

    public IHighlightingDefinition? Resolve(DocumentKind kind)
    {
        if (_cache.TryGetValue(kind, out var cached)) return cached;
        var def = kind switch
        {
            DocumentKind.XQuery => LoadEmbedded("XQuery.xshd"),
            DocumentKind.Xslt => HighlightingManager.Instance.GetDefinition("XML"),
            _ => null,
        };
        _cache[kind] = def;
        return def;
    }

    private static IHighlightingDefinition? LoadEmbedded(string name)
    {
        var asm = typeof(HighlightingRegistry).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(name, StringComparison.Ordinal));
        if (resourceName is null) return null;
        using var s = asm.GetManifestResourceStream(resourceName)!;
        using var reader = XmlReader.Create(s);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
```

- [ ] **Step 4: Apply in `TextEditorView.Bind`**

```csharp
public void Bind(TextDocument document, HighlightingRegistry highlighting)
{
    ...
    editor.SyntaxHighlighting = highlighting.Resolve(document.Kind);
    ...
}
```

- [ ] **Step 5: Write 3 registry tests**

- Returns null for `DocumentKind.Unknown`
- Returns non-null for `DocumentKind.XQuery`
- Result is cached (same instance on repeat calls)

- [ ] **Step 6: Run**

---

### Task 4: LSP completion popup in text view

**Files:**
- Modify: `src/Phoenixml.Editor/Lsp/LspClient.cs` (add `RequestCompletionAsync`)
- Modify: `src/Phoenixml.Editor/Lsp/LspMessages.cs` (Completion request DTOs)
- Modify: `src/Phoenixml.Editor/Views/TextEditorView.axaml` (popup)
- Modify: `src/Phoenixml.Editor/Views/TextEditorView.axaml.cs` (Ctrl+Space handling)
- Modify: `src/Phoenixml.Editor/Views/TextEditorView` connection to `LspSession`

- [ ] **Step 1: Extend `LspMessages` with request DTOs**

```csharp
public sealed record LspTextDocumentPositionParams(
    [property: JsonPropertyName("textDocument")] LspTextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("position")] LspPosition Position);

public sealed record LspCompletionItem(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("kind")] int Kind);

public sealed record LspCompletionList(
    [property: JsonPropertyName("isIncomplete")] bool IsIncomplete,
    [property: JsonPropertyName("items")] LspCompletionItem[] Items);
```

- [ ] **Step 2: Add `LspClient.RequestCompletionAsync`**

```csharp
public Task<LspCompletionList?> RequestCompletionAsync(string uri, int line, int character) =>
    _rpc.InvokeAsync<LspCompletionList?>("textDocument/completion",
        new LspTextDocumentPositionParams(
            new LspTextDocumentIdentifier(uri),
            new LspPosition(line, character)));
```

- [ ] **Step 3: Add `LspSession.RequestCompletionAsync`**

Forwards to the client with the doc's URI + given position. Returns a flat `IReadOnlyList<string>` of labels for the popup.

- [ ] **Step 4: Popup AXAML**

In `TextEditorView.axaml`, add a `Popup` inside a `Panel` wrap:

```xml
<Panel>
  <ae:TextEditor Name="Editor" ... />
  <Popup Name="CompletionPopup" PlacementTarget="{Binding ElementName=Editor}" Placement="Pointer" IsLightDismissEnabled="True">
    <Border Background="#252526" BorderBrush="#4A4A4F" BorderThickness="1" Padding="4">
      <ListBox Name="CompletionList" MaxHeight="240" Width="280"
               Background="Transparent" FontFamily="Cascadia Mono, Consolas, monospace" />
    </Border>
  </Popup>
</Panel>
```

- [ ] **Step 5: Trigger on Ctrl+Space; accept on Tab/Enter; close on Esc/blur**

```csharp
editor.TextArea.KeyDown += async (s, e) =>
{
    if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
    {
        await ShowCompletionAsync();
        e.Handled = true;
    }
    else if (popup.IsOpen)
    {
        if (e.Key == Key.Tab || e.Key == Key.Enter)
        {
            AcceptCompletion();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            popup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Up) { completionList.SelectedIndex = Math.Max(0, completionList.SelectedIndex - 1); e.Handled = true; }
        else if (e.Key == Key.Down) { completionList.SelectedIndex = Math.Min(completionList.Items.Count - 1, completionList.SelectedIndex + 1); e.Handled = true; }
    }
};
```

`ShowCompletionAsync` asks the session, populates the listbox, opens the popup.
`AcceptCompletion` inserts the selected item's label at the caret.

- [ ] **Step 6: Build + smoke (manual; no unit test for UI popup)**

---

### Task 5: LSP hover popup

**Files:**
- Modify: `src/Phoenixml.Editor/Lsp/LspClient.cs` (`RequestHoverAsync`)
- Modify: `src/Phoenixml.Editor/Lsp/LspMessages.cs` (Hover DTO)
- Modify: `src/Phoenixml.Editor/Views/TextEditorView.axaml(.cs)` (Hover popup, idle-delay trigger)

- [ ] **Step 1: Add Hover DTO**

```csharp
public sealed record LspHover(
    [property: JsonPropertyName("contents")] string Contents);
```

- [ ] **Step 2: `RequestHoverAsync`**

Same pattern as completion. Returns `LspHover?` (null if no info).

- [ ] **Step 3: Hover popup AXAML**

A second `Popup` inside the same wrap. Smaller; just a `TextBlock` for `Contents`.

- [ ] **Step 4: Idle-delay trigger**

```csharp
// 500ms after caret stops moving, ask the server
CancellationTokenSource? hoverDebounce;
editor.TextArea.Caret.PositionChanged += (_, _) =>
{
    hoverDebounce?.Cancel();
    hoverDebounce = new CancellationTokenSource();
    _ = ShowHoverAfterDelayAsync(hoverDebounce.Token);
};
```

- [ ] **Step 5: Build + smoke**

---

### Task 6: Find/Replace in text view

**Files:**
- Create: `src/Phoenixml.Editor/Find/TextFindController.cs`
- Create: `test/Phoenixml.Editor.Tests/Find/TextFindControllerTests.cs`
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs` (Ctrl+F routes by doc kind)
- Modify: `src/Phoenixml.Editor/Views/TextEditorView.axaml.cs` (selection-range highlighting)

**Approach:** When the active doc is text, Ctrl+F shows the existing `FindBar` but routes through a new `TextFindController` that searches `TextDocument.Text` and applies AvaloniaEdit `TextSegmentCollection` selections for highlight + cycle.

- [ ] **Step 1: Write 5 controller tests**

```csharp
// - Find with empty query → empty result
// - Find substring → list of (offset, length) ranges
// - Match-case toggle filters correctly
// - Replace single occurrence
// - Replace all
```

- [ ] **Step 2: Implement `TextFindController`**

Pure logic over a string + query. No UI deps. Returns `IReadOnlyList<(int Offset, int Length)>`.

- [ ] **Step 3: Wire into `TextEditorView`**

Add a public `ApplyMatches(IReadOnlyList<(int, int)>)` method that paints segments via `TextEditor.TextArea.TextView.LineTransformers` (AvaloniaEdit's standard mechanism). Active match scrolls into view + selects.

- [ ] **Step 4: Route Ctrl+F in `EditorMainWindow`**

`ShowFindBar` checks the active doc kind. For text, switches the bar's `QueryChanged` event to call `TextFindController.Find` and apply matches.

- [ ] **Step 5: Run tests**

---

### Task 7: Undo/Redo bridging + LSP crash recovery

**Files:**
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs` (Ctrl+Z routes by doc kind)
- Modify: `src/Phoenixml.Editor/Views/TextEditorView.axaml.cs` (expose `Undo()` / `Redo()`)
- Modify: `src/Phoenixml.Editor/Lsp/LspServerProcess.cs` (`Exited` event)
- Modify: `src/Phoenixml.Editor/Lsp/LspSession.cs` (restart logic)

- [ ] **Step 1: TextEditorView Undo/Redo proxies**

```csharp
public bool CanUndo => editor.CanUndo;
public bool CanRedo => editor.CanRedo;
public void Undo() => editor.Undo();
public void Redo() => editor.Redo();
```

- [ ] **Step 2: Ctrl+Z routing in EditorMainWindow**

```csharp
_window.KeyBindings.Add(new KeyBinding {
    Gesture = new KeyGesture(Key.Z, KeyModifiers.Control),
    Command = new RelayCommand(() => {
        if (_state.ActiveDocument is Phoenixml.Platform.Editor.Text.TextDocument)
            _textEditor.Undo();
        else
            UndoCommand.Execute(this);
    }),
});
```

Same for Ctrl+Y.

- [ ] **Step 3: LspServerProcess.Exited**

```csharp
_proc.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
public event EventHandler? Exited;
```

- [ ] **Step 4: LspSession restart**

When `Exited` fires unexpectedly (not during dispose): build a fresh `LspServerProcess`, build a fresh `LspClient`, re-send `initialize` + `didOpen` with the current buffer.

- [ ] **Step 5: Smoke**

---

### Task 8: MSBuild auto-publish for LSP server binaries

**Files:**
- Modify: `src/Phoenixml.Editor/Phoenixml.Editor.csproj`

- [ ] **Step 1: Add `PublishLspServers` target**

```xml
<Target Name="PublishLspServers" BeforeTargets="Build">
  <PropertyGroup>
    <_LspToolsDir>$(OutputPath)tools</_LspToolsDir>
    <_XQueryLspSrc>$(MSBuildThisFileDirectory)../../../phoenixmldb/phoenixmldb-xquery/src/PhoenixmlDb.XQuery.LanguageServer</_XQueryLspSrc>
    <_XsltLspSrc>$(MSBuildThisFileDirectory)../../../phoenixmldb/phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer</_XsltLspSrc>
  </PropertyGroup>
  <Exec Condition="Exists('$(_XQueryLspSrc)') And !Exists('$(_LspToolsDir)/phoenixmldb-xquery-lsp')"
        Command="dotnet publish &quot;$(_XQueryLspSrc)&quot; -c Release -o &quot;$(_LspToolsDir)&quot;" />
  <Exec Condition="Exists('$(_XsltLspSrc)') And !Exists('$(_LspToolsDir)/phoenixmldb-xslt-lsp')"
        Command="dotnet publish &quot;$(_XsltLspSrc)&quot; -c Release -o &quot;$(_LspToolsDir)&quot;" />
</Target>
```

- [ ] **Step 2: Test by running `dotnet build`**

Verify `bin/Debug/net10.0/tools/phoenixmldb-xquery-lsp` exists after build.

---

### Task 9: Walkthrough + commit

**Files:**
- Create: `docs/editor-v0.17-walkthrough.md`

- [ ] **Step 1: Write walkthrough**
- [ ] **Step 2: Full test run** — expected: green + 12 new tests (4 bridge, 5 find, 3 registry)
- [ ] **Step 3: Commit**

---

## Risk register

- **`.xshd` regex compatibility** — AvaloniaEdit's syntax engine uses its own regex flavor. The XQuery `.xshd` may need tweaks (lookbehind unsupported, etc.). Test with a real file.
- **`HighlightingManager.Instance.GetDefinition("XML")`** may return null when AvaloniaEdit isn't fully initialized at the time we ask. Defer resolution to first `Bind` call.
- **Completion popup placement** — `Placement="Pointer"` follows the mouse, not the caret. The right placement is anchored at the caret's screen position, which AvaloniaEdit exposes via `TextArea.TextView.GetVisualPosition(...)`. May need a custom placement helper.
- **Hover debounce + cancellation** — easy to leak `CancellationTokenSource`s. Use a single field, dispose previous.
- **LSP restart on crash** — naïve restart can loop forever if the server crashes on startup. Add a backoff + max-retries (3 within 30s).
- **MSBuild `BeforeBuild` running `dotnet publish`** — slow first build, fast incremental. The `Condition="!Exists(...)"` guard makes subsequent builds skip the publish.
