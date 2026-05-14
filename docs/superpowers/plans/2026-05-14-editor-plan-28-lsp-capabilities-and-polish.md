# Editor v0.18 — Plan 28 LSP Capabilities + Polish Pack — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the LSP servers (XQuery + XSLT) with three high-value capabilities and finish the four UX polish carryovers from Plan 27. After this plan: `documentSymbol` powers an outline view, `signatureHelp` shows parameter info as you type, `definition` jumps to local declarations, Find/Replace works in text view, popups anchor at the caret, the server restart-with-backoff makes crashes recoverable, and a shared LSP DTO library deduplicates the three copies.

**Architecture:**

- **Server `documentSymbol`** — both servers add a handler that walks their AST and returns a flat list of `DocumentSymbol`s (function declarations, variable declarations, template matches, named templates). XQuery uses `XQueryExpression` walk; XSLT walks compiled stylesheet declarations.
- **Server `signatureHelp`** — both servers detect when the caret is inside a function-call's parens and return the signature string. XQuery looks up the function in `FunctionLibrary` + `XPathSignatures`-equivalent (server-side copy). XSLT returns a stub (functions are rarer in templates).
- **Server `definition` (local)** — XQuery: when the cursor is on a `$variable` or unprefixed function call, look up the matching `declare variable` / `declare function` in the same buffer and return its source location. Cross-module / cross-file definition is Plan 29.
- **Editor outline view** — new `OutlineView` (left rail or new bottom-strip section) bound to the active text doc's symbol list. Clicking a symbol moves the caret + scrolls into view.
- **Editor signature-help popup** — listens for `(` and `,` keystrokes, asks the server, renders a one-line tooltip above the caret.
- **Editor F12** — Ctrl+click or F12 asks server for definition, scrolls the caret to the result.
- **Find/Replace in text view** — extend `FindBar` to recognize text docs; route `QueryChanged` to `TextFindController.Find`; apply matches via AvaloniaEdit `TextSegmentCollection`-backed `IBackgroundRenderer`.
- **Caret-anchored popups** — `TextEditor.TextArea.TextView.GetVisualPosition(caret, VisualYPosition.LineBottom)` converts caret → on-screen point; popups use absolute placement.
- **Restart with backoff** — `LspSession` catches `Exited`, schedules restart with exponential backoff (1s, 3s, 9s); gives up after 3 attempts; logs via NotifyAction.
- **Shared `PhoenixmlDb.Lsp.Common` library** — extract LSP DTOs (Position, Range, Diagnostic, etc.) into a new NuGet-free shared project referenced by both servers + the editor.

**Tech Stack:** Avalonia 11.2.4, AvaloniaEdit 11.2.0, StreamJsonRpc, PhoenixmlDb.Core/XQuery/Xslt + LanguageServer projects.

**Out of scope (Plan 29+):**
- `textDocument/references` (lexical analysis)
- `textDocument/formatting` (pretty-printer per language)
- Cross-file / cross-module `definition`
- Code actions / quick fixes
- Semantic tokens
- Rename refactoring

---

## File structure (top-level)

### Shared LSP library

```
phoenixmldb-xquery/src/PhoenixmlDb.Lsp.Common/
  PhoenixmlDb.Lsp.Common.csproj
  Position.cs, Range.cs, Diagnostic.cs, ...   (extracted from both servers)
```

(Hosted in phoenixmldb-xquery for convenience; XSLT references it via project ref.)

### XQuery server additions

```
phoenixmldb-xquery/src/PhoenixmlDb.XQuery.LanguageServer/
  Handlers/
    DocumentSymbolHandler.cs
    SignatureHelpHandler.cs
    DefinitionHandler.cs
```

### XSLT server additions

```
phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/
  Handlers/
    DocumentSymbolHandler.cs   (templates + variables + imports)
```

### Editor

```
phoenixml-platform/src/Phoenixml.Editor/
  Views/
    OutlineView.axaml(.cs)            (NEW — list of document symbols)
    TextEditorView.axaml(.cs)         (modify — caret-anchored popups, F12)
  Find/
    TextFindController.cs             (existing)
    FindBar.axaml.cs                  (modify — accept text-doc adapter)
    TextSegmentSelectionRenderer.cs   (NEW — AvaloniaEdit IBackgroundRenderer for find highlights)
  Lsp/
    LspClient.cs                      (modify — RequestDocumentSymbolsAsync, RequestDefinitionAsync, RequestSignatureHelpAsync)
    LspMessages.cs                    (modify — DocumentSymbol, Location, SignatureHelp DTOs)
    LspSession.cs                     (modify — restart-with-backoff)
  EditorMainWindow.cs                 (modify — F12 binding, OutlineView host, restart wiring)
```

---

## Tasks

### Task 1: Shared `PhoenixmlDb.Lsp.Common` library

**Approach:** Extract the LSP message DTOs (Position, Range, Diagnostic, etc.) into a new library hosted in `phoenixmldb-xquery/src/`. Update both LSP servers and the editor to reference it.

**Files:**
- Create: `phoenixmldb-xquery/src/PhoenixmlDb.Lsp.Common/PhoenixmlDb.Lsp.Common.csproj`
- Create: `phoenixmldb-xquery/src/PhoenixmlDb.Lsp.Common/LspMessages.cs` (move from XQuery server, expand)
- Modify: both LSP server csprojs → ProjectReference to common
- Delete: `Lsp/LspMessages.cs` in both servers
- Modify: editor's `LspMessages.cs` → either reference Common or keep separate (skip cross-repo ref for editor; keep editor's copy)

- [ ] **Step 1: Create the common project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>PhoenixmlDb.Lsp.Common</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Description>Shared LSP message DTOs used by the XQuery + XSLT language servers.</Description>
    <NoWarn>$(NoWarn);CA1054;CA1056;CA1819;CA1515;CA1724</NoWarn>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Copy LSP DTOs from XQuery server into Common**

Move the existing `Lsp/LspMessages.cs` content (Position, Range, Diagnostic, TextDocumentItem, etc.) into `PhoenixmlDb.Lsp.Common/LspMessages.cs`. Namespace becomes `PhoenixmlDb.Lsp.Common`.

Add new types needed by Plan 28:

```csharp
public sealed record DocumentSymbol(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] int Kind,
    [property: JsonPropertyName("range")] Range Range,
    [property: JsonPropertyName("selectionRange")] Range SelectionRange);

public sealed record Location(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("range")] Range Range);

public sealed record SignatureInformation(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("documentation")] string? Documentation);

public sealed record SignatureHelp(
    [property: JsonPropertyName("signatures")] SignatureInformation[] Signatures,
    [property: JsonPropertyName("activeSignature")] int ActiveSignature,
    [property: JsonPropertyName("activeParameter")] int ActiveParameter);

public sealed record DocumentSymbolParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument);

// SymbolKind constants (LSP 3.17 §SymbolKind)
public static class SymbolKind
{
    public const int Function = 12;
    public const int Variable = 13;
    public const int Class = 5;
    public const int Module = 2;
    public const int Method = 6;
}
```

- [ ] **Step 3: Update both servers to use Common**

- Delete each server's `Lsp/LspMessages.cs` (or convert to type aliases for backwards compat)
- Add `<ProjectReference Include="../PhoenixmlDb.Lsp.Common/PhoenixmlDb.Lsp.Common.csproj" />` to both server csprojs
- Update `using` statements: replace `using PhoenixmlDb.{XQuery,Xslt}.LanguageServer.Lsp` with `using PhoenixmlDb.Lsp.Common`

For XSLT server, the project reference crosses repos. Use a relative path: `../../../phoenixmldb-xquery/src/PhoenixmlDb.Lsp.Common/...`. If that's awkward (it is), leave XSLT's DTOs in-place and only dedupe XQuery → Common for now.

- [ ] **Step 4: Editor keeps its own DTOs**

Cross-repo ProjectReference from the editor to a phoenixmldb-xquery project is unprecedented in this codebase and breaks the editor's standalone build story. The editor keeps its own DTO copies (they're small — Plan 28's `LspMessages.cs` extensions go alongside).

- [ ] **Step 5: Build + run server tests**

Run: `cd /raid/elvogel/repos/phoenixmldb && dotnet test phoenixmldb-xquery/tests/PhoenixmlDb.XQuery.LanguageServer.Tests --nologo`
Expected: 4/4 still green.

---

### Task 2: `textDocument/documentSymbol` on the XQuery server

**Files:**
- Modify: `phoenixmldb-xquery/src/PhoenixmlDb.XQuery.LanguageServer/XQueryLanguageServer.cs`
- Create: `phoenixmldb-xquery/src/PhoenixmlDb.XQuery.LanguageServer/Handlers/DocumentSymbolHandler.cs`
- Create: `phoenixmldb-xquery/tests/PhoenixmlDb.XQuery.LanguageServer.Tests/DocumentSymbolTests.cs`

**Approach:** Parse the buffer via `XQueryParserFacade`, walk the resulting `XQueryExpression` collecting `FunctionDeclaration`, `VariableDeclaration`, and `ModuleImport` nodes. Each yields a `DocumentSymbol` with `Name`, `Kind`, and `Range` derived from the AST node's `SourceLocation`.

- [ ] **Step 1: Inspect available AST types**

Run: `grep -rn "FunctionDeclaration\|VariableDeclaration\|class.*Declaration" phoenixmldb-xquery/src/PhoenixmlDb.XQuery/Ast/ | head -10`

Identify the AST node names and their `Location` properties. Adapt the handler accordingly — the exact names matter.

- [ ] **Step 2: Implement `DocumentSymbolHandler`**

```csharp
public static class DocumentSymbolHandler
{
    public static DocumentSymbol[] Handle(DocumentBuffer buf)
    {
        var parser = new XQueryParserFacade();
        XQueryExpression ast;
        try { ast = parser.Parse(buf.Text); }
        catch { return Array.Empty<DocumentSymbol>(); }

        var symbols = new List<DocumentSymbol>();
        Walk(ast, symbols);
        return symbols.ToArray();
    }

    private static void Walk(XQueryExpression node, List<DocumentSymbol> sink)
    {
        // Match on the concrete AST node types. Each match adds one DocumentSymbol.
        switch (node)
        {
            case FunctionDeclaration fn:
                sink.Add(new DocumentSymbol(
                    Name: fn.Name?.LocalName ?? "<anon>",
                    Kind: SymbolKind.Function,
                    Range: ToRange(fn.Location),
                    SelectionRange: ToRange(fn.Location)));
                break;
            case VariableDeclaration vd:
                sink.Add(new DocumentSymbol(
                    Name: vd.Name?.LocalName ?? "<anon>",
                    Kind: SymbolKind.Variable,
                    Range: ToRange(vd.Location),
                    SelectionRange: ToRange(vd.Location)));
                break;
            // Recurse into children — depends on AST shape; use a visitor pattern if available.
        }
    }

    private static Range ToRange(SourceLocation? loc)
    {
        if (loc is null) return new Range(new Position(0, 0), new Position(0, 1));
        var start = new Position(Math.Max(0, loc.Line - 1), Math.Max(0, loc.Column));
        var end = new Position(start.Line, start.Character + loc.Length);
        return new Range(start, end);
    }
}
```

The exact AST traversal depends on the engine's API. If the engine doesn't expose a visitor, walk reflectively or iterate top-level statements only (MVP).

- [ ] **Step 3: Wire into `XQueryLanguageServer`**

```csharp
[JsonRpcMethod("textDocument/documentSymbol")]
public DocumentSymbol[] DocumentSymbol(DocumentSymbolParams p)
{
    if (!_buffers.TryGetValue(p.TextDocument.Uri, out var buf)) return Array.Empty<DocumentSymbol>();
    return DocumentSymbolHandler.Handle(buf);
}
```

Also advertise the capability in `Initialize`: add `DocumentSymbolProvider = true` to `ServerCapabilities`.

- [ ] **Step 4: Write 3 tests**

- Empty buffer → empty list
- `declare function local:foo() { 1 };` → one Function symbol named "foo"
- `declare variable $x := 1; declare function local:f() { 2 };` → two symbols (one Variable, one Function)

- [ ] **Step 5: Run tests**

---

### Task 3: `textDocument/documentSymbol` on the XSLT server

**Files:**
- Modify: `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/XsltLanguageServer.cs`
- Create: `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/Handlers/DocumentSymbolHandler.cs`
- Create: tests

**Approach:** Parse the stylesheet via `XsltTransformer.LoadStylesheetAsync` (catch exceptions), then enumerate the compiled stylesheet's templates, variables, and imports. The XSLT AST exposes these via `XsltStylesheet.Templates`, `Variables`, `Imports` (verify exact names by inspection).

- [ ] **Step 1: Inspect XSLT AST**

Run: `grep -rn "class XsltStylesheet\|public.*Templates\|public.*Variables" phoenixmldb-xslt/src/PhoenixmlDb.Xslt/Ast/`

- [ ] **Step 2: Implement handler** (same shape as XQuery handler, different AST types)
- [ ] **Step 3: Wire into server, advertise capability**
- [ ] **Step 4: 3 tests** — empty, one named template, multiple templates
- [ ] **Step 5: Run tests**

---

### Task 4: Outline view in editor

**Files:**
- Create: `src/Phoenixml.Editor/Views/OutlineView.axaml(.cs)`
- Modify: `src/Phoenixml.Editor/Lsp/LspClient.cs` — `RequestDocumentSymbolsAsync`
- Modify: `src/Phoenixml.Editor/Lsp/LspSession.cs` — `RequestDocumentSymbolsAsync`
- Modify: `src/Phoenixml.Editor/Lsp/LspMessages.cs` — `DocumentSymbol`, `DocumentSymbolParams`
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs` — host the OutlineView in a docked panel for text docs

- [ ] **Step 1: LSP DTOs in editor**

Mirror the Common library's `DocumentSymbol` shape inside the editor's `LspMessages.cs`.

- [ ] **Step 2: `LspClient.RequestDocumentSymbolsAsync`**

```csharp
public Task<LspDocumentSymbol[]?> RequestDocumentSymbolsAsync(string uri) =>
    _rpc.InvokeAsync<LspDocumentSymbol[]?>("textDocument/documentSymbol",
        new { textDocument = new LspTextDocumentIdentifier(uri) });
```

- [ ] **Step 3: `OutlineView.axaml` + code-behind**

A simple `ListBox` bound to `IReadOnlyList<LspDocumentSymbol>`. Selection raises `SymbolActivated` event with the symbol's range.

```xml
<UserControl ...>
  <ListBox Name="SymbolList">
    <ListBox.ItemTemplate>
      <DataTemplate>
        <TextBlock Text="{Binding Name}" FontFamily="Cascadia Mono, Consolas, monospace" />
      </DataTemplate>
    </ListBox.ItemTemplate>
  </ListBox>
</UserControl>
```

- [ ] **Step 4: Host in EditorMainWindow**

When `SwapCentralPane` activates a text doc, host the `OutlineView` in a 200dip-wide column to the right of the central pane (or in the bottom strip for a horizontal layout). Re-query on every `didChange` (debounced 500ms).

Wire `OutlineView.SymbolActivated` → `_textEditor.MoveCaretTo(line, column)`.

- [ ] **Step 5: Smoke (no unit tests for UI)**

---

### Task 5: `textDocument/signatureHelp`

**Files:**
- Modify: `phoenixmldb-xquery/src/PhoenixmlDb.XQuery.LanguageServer/XQueryLanguageServer.cs`
- Create: `phoenixmldb-xquery/src/PhoenixmlDb.XQuery.LanguageServer/Handlers/SignatureHelpHandler.cs`
- Modify: editor's `LspClient` + `LspSession` + `TextEditorView` (trigger on `(`)
- Tests

**Approach:** Server-side: when called, look backward from caret position for the nearest unmatched `(`. Read the function name immediately before. Look up signature. Count commas between `(` and caret for `activeParameter`. Return `SignatureHelp` with one signature.

XSLT server returns null (functions in xsl: namespace are rarer; defer to a future plan).

- [ ] **Step 1: Curated signature table on the server**

Hard-coded in `SignatureHelpHandler` for MVP — same ~35 functions as `XPathSignatures` in the editor:

```csharp
private static readonly Dictionary<string, string> Signatures = new(StringComparer.Ordinal)
{
    ["count"] = "count($arg as item()*) as xs:integer",
    ["string"] = "string($arg as item()?) as xs:string",
    // ... etc.
};
```

- [ ] **Step 2: Implement handler**

```csharp
public static SignatureHelp? Handle(DocumentBuffer buf, Position pos)
{
    var offset = buf.PositionToOffset(pos);
    if (offset <= 0) return null;
    // Walk back to find the open-paren, count commas for activeParameter
    int depth = 0;
    int activeParam = 0;
    int parenIndex = -1;
    for (int i = offset - 1; i >= 0; i--)
    {
        var c = buf.Text[i];
        if (c == ')') depth++;
        else if (c == ',' && depth == 0) activeParam++;
        else if (c == '(')
        {
            if (depth == 0) { parenIndex = i; break; }
            depth--;
        }
    }
    if (parenIndex <= 0) return null;
    int start = parenIndex;
    while (start > 0 && IsWordChar(buf.Text[start - 1])) start--;
    var fnName = buf.Text.Substring(start, parenIndex - start);
    if (!Signatures.TryGetValue(fnName, out var sig)) return null;
    return new SignatureHelp(
        Signatures: [new SignatureInformation(sig, Documentation: null)],
        ActiveSignature: 0,
        ActiveParameter: activeParam);
}
```

- [ ] **Step 3: Add `DocumentBuffer.PositionToOffset` helper**

Inverse of `OffsetToPosition`. Walks lines until `pos.Line` then adds `pos.Character`.

- [ ] **Step 4: Wire JSON-RPC handler + advertise capability**

```csharp
[JsonRpcMethod("textDocument/signatureHelp")]
public SignatureHelp? SignatureHelp(LspTextDocumentPositionParams p)
{
    if (!_buffers.TryGetValue(p.TextDocument.Uri, out var buf)) return null;
    return SignatureHelpHandler.Handle(buf, p.Position);
}
```

In `Initialize`, add `SignatureHelpProvider = new SignatureHelpOptions(TriggerCharacters: ["(", ","])`.

- [ ] **Step 5: Editor client method**

In `LspClient.cs`:

```csharp
public Task<LspSignatureHelp?> RequestSignatureHelpAsync(string uri, int line, int character) =>
    _rpc.InvokeAsync<LspSignatureHelp?>("textDocument/signatureHelp",
        new LspTextDocumentPositionParams(
            new LspTextDocumentIdentifier(uri),
            new LspPosition(line, character)));
```

In `LspSession`, similar wrapper.

- [ ] **Step 6: Wire popup in `TextEditorView`**

On `(` typed: request signatureHelp; show in a popup similar to hover. Close on `)` or `Escape`. Reuse the existing `HoverPopup` (rename to `InfoPopup`) or add a third popup.

- [ ] **Step 7: 3 server tests + manual smoke**

---

### Task 6: `textDocument/definition` (local-only)

**Files:**
- Modify: `phoenixmldb-xquery/src/PhoenixmlDb.XQuery.LanguageServer/XQueryLanguageServer.cs`
- Create: `phoenixmldb-xquery/src/PhoenixmlDb.XQuery.LanguageServer/Handlers/DefinitionHandler.cs`
- Modify: editor client + session + `TextEditorView` (F12 binding)

**Approach:** Server-side: parse the buffer, find the token at the given position. If it's a `$variable`, scan declarations + `for $x in` / `let $x :=` bindings for the matching name; return the binding's `Location`. If it's an unprefixed identifier followed by `(`, scan `declare function local:X` for the matching name. If nothing matches, return null.

Cross-file resolution (imports / external modules) is **Plan 29**.

- [ ] **Step 1: Implement `DefinitionHandler` (skeleton)**

```csharp
public static Location? Handle(DocumentBuffer buf, Position pos)
{
    var offset = buf.PositionToOffset(pos);
    var token = ExtractTokenAt(buf.Text, offset);
    if (token is null) return null;

    var declRange = FindDeclaration(buf.Text, token);
    if (declRange is null) return null;
    return new Location(buf.Uri, declRange);
}

private static Range? FindDeclaration(string text, (bool IsVariable, string Name) token)
{
    // Variable: search for "declare variable $foo" or "for $foo in" or "let $foo :="
    // Function: search for "declare function (prefix:)?foo("
    // Return the range of the declaration name token.
    // ...
}
```

Token extraction: scan backward + forward from `offset` for word characters; check if preceded by `$` for variable.

Declaration search: line-scan with regex for now (parser-driven would be richer but parser may fail on incomplete buffers; regex is robust).

- [ ] **Step 2: Wire JSON-RPC handler + capability**

`Initialize` advertises `DefinitionProvider = true`. Handler:

```csharp
[JsonRpcMethod("textDocument/definition")]
public Location? Definition(LspTextDocumentPositionParams p)
{
    if (!_buffers.TryGetValue(p.TextDocument.Uri, out var buf)) return null;
    return DefinitionHandler.Handle(buf, p.Position);
}
```

- [ ] **Step 3: Editor F12 binding**

In `TextEditorView`, bind F12:

```csharp
editor.KeyDown += async (_, e) =>
{
    if (e.Key == Key.F12 && _session is not null)
    {
        var caret = editor.TextArea.Caret;
        var loc = await _session.RequestDefinitionAsync(caret.Line - 1, caret.Column - 1);
        if (loc is not null)
            editor.TextArea.Caret.Location = new TextLocation(loc.Range.Start.Line + 1, loc.Range.Start.Character + 1);
        e.Handled = true;
    }
};
```

- [ ] **Step 4: 4 server tests** — variable decl found; function decl found; for-binding found; unknown token returns null
- [ ] **Step 5: Run**

---

### Task 7: Caret-anchored popups

**Files:**
- Modify: `src/Phoenixml.Editor/Views/TextEditorView.axaml.cs`

**Approach:** Replace `Placement="Pointer"` on completion/hover popups with absolute positioning computed from `TextEditor.TextArea.TextView.GetVisualPosition(caret, VisualYPosition.LineBottom)`. Convert the visual point to screen coordinates via `editor.TranslateToScreen(...)`. Apply via `HorizontalOffset` / `VerticalOffset` properties on the `Popup`.

- [ ] **Step 1: Helper to compute caret-to-screen point**

```csharp
private Avalonia.Point GetCaretScreenPoint()
{
    var editor = this.FindControl<TextEditor>("Editor")!;
    var caret = editor.TextArea.Caret;
    var visualPos = editor.TextArea.TextView.GetVisualPosition(caret, VisualYPosition.LineBottom);
    // Translate text-view-relative → control-relative → screen
    var controlPoint = visualPos - editor.TextArea.TextView.ScrollOffset;
    return controlPoint;
}
```

- [ ] **Step 2: Apply when opening popups**

```csharp
var point = GetCaretScreenPoint();
popup.HorizontalOffset = point.X;
popup.VerticalOffset = point.Y;
popup.Placement = PlacementMode.AnchorAndGravity;
popup.PlacementAnchor = PopupAnchor.TopLeft;
popup.IsOpen = true;
```

(Avalonia API names verify on first build — may need adjustment.)

- [ ] **Step 3: Smoke**

---

### Task 8: Find/Replace UI for text view

**Files:**
- Modify: `src/Phoenixml.Editor/Find/FindBar.axaml.cs` — accept a generic search adapter
- Create: `src/Phoenixml.Editor/Find/TextFindAdapter.cs` — bridges FindBar events to `TextFindController` + AvaloniaEdit
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs` — Ctrl+F routes by doc kind

**Approach:** Currently `FindBar` is wired directly to `FindController` (XML). Add an `IFindAdapter` interface; have `FindController` implement it (or wrap it); add `TextFindAdapter` that drives `TextFindController` + AvaloniaEdit selection highlighting. `EditorMainWindow.ShowFindBar` picks the adapter based on the active doc kind.

For MVP we can stub the AvaloniaEdit selection highlighting with simple `editor.SelectionStart`/`editor.SelectionLength` (selects the active match). Multi-match background highlighting via `IBackgroundRenderer` is more polish; defer.

- [ ] **Step 1: `TextFindAdapter`**

Holds a `TextDocument` + `TextEditor` ref. `OnQueryChanged` runs `TextFindController.Find`, stores ranges, selects the first match. `OnNext`/`OnPrevious` cycle.

- [ ] **Step 2: `FindBar` indirection**

`FindBar` currently invokes `FindController` directly via events. Refactor to fire events the bar exposes (`QueryChanged`, `NextRequested`, etc.); EditorMainWindow subscribes the right controller based on active doc kind.

(In practice the bar already exposes these events — they're consumed by `FindController.OnQueryChanged` etc. So this task is purely about adding a parallel subscription path.)

- [ ] **Step 3: Routing in EditorMainWindow**

```csharp
internal void ShowFindBar()
{
    if (_state.ActiveDocument is Phoenixml.Platform.Editor.Text.TextDocument text)
    {
        _findBar.IsVisible = true;
        _findBar.FocusQueryBox();
        _textFindAdapter.Bind(text, _textEditor);
        // _findController doesn't subscribe; _textFindAdapter does
    }
    else
    {
        // Existing XML flow
    }
}
```

- [ ] **Step 4: Smoke**

---

### Task 9: LSP server auto-restart with backoff

**Files:**
- Modify: `src/Phoenixml.Editor/Lsp/LspSession.cs`
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs`

**Approach:** When `LspServerProcess.Exited` fires unexpectedly (during a session, not during dispose): try up to 3 restarts with delays 1s, 3s, 9s. Each restart spawns a fresh `LspServerProcess`, builds a fresh `LspClient`, re-sends `initialize` + `didOpen`. If all 3 fail, surface a permanent error via NotifyAction and give up.

- [ ] **Step 1: `LspSession.SetRestartPolicy(Func<LspServerProcess>)`**

Lets the session know how to spawn a replacement process. Plus internal retry counter.

- [ ] **Step 2: Restart logic on Exited**

```csharp
private async Task TryRestartAsync()
{
    if (_disposed) return;
    if (_restartAttempts >= 3) { _onPermanentFailure?.Invoke(); return; }
    var delay = TimeSpan.FromSeconds(Math.Pow(3, _restartAttempts));
    _restartAttempts++;
    await Task.Delay(delay);
    if (_disposed) return;
    // Build new process + client, re-initialize, re-didOpen
}
```

- [ ] **Step 3: Wire into EditorMainWindow**

Pass the registry path + permanent-failure callback (which calls `NotifyAction` with the give-up message).

- [ ] **Step 4: Smoke (manual: kill the LSP child externally, see editor recover)**

---

### Task 10: Walkthrough + commits across 3 repos

**Files:**
- Create: `phoenixml-platform/docs/editor-v0.18-walkthrough.md`

Per-repo commits:
- `phoenixmldb-xquery`: documentSymbol + signatureHelp + definition handlers + Lsp.Common library
- `phoenixmldb-xslt`: documentSymbol handler
- `phoenixml-platform`: editor wiring (OutlineView, F12, popups, FindBar routing, restart-with-backoff)

- [ ] **Step 1: Write walkthrough**
- [ ] **Step 2: Full test run** — all green; +12-15 new tests
- [ ] **Step 3: Per-repo commits**

---

## Risk register

- **AST shape assumptions** — `FunctionDeclaration`, `VariableDeclaration` type names are best-guess; verify by grep in Task 2/3 step 1 before implementing handlers.
- **Cross-repo `ProjectReference`** — XSLT server referencing `PhoenixmlDb.Lsp.Common` in phoenixmldb-xquery is fragile (assumes sibling checkout). Task 1 step 3 documents the fallback (keep XSLT DTOs in-place).
- **AvaloniaEdit caret-to-screen API names** — `GetVisualPosition` / `ScrollOffset` / `PopupAnchor.TopLeft` are best-guess. Iterate at first build.
- **F12 binding conflicts** — Avalonia's `TextEditor` may already consume F12; check before adding.
- **Restart loop danger** — even with the 3-attempt cap, a misconfigured server can spam the user with "exited unexpectedly" notifications. Ensure `_restartAttempts` is reset only on successful initialize, not on every Exited.
- **`signatureHelp` activeParameter under nested parens** — the comma-counting algorithm only counts commas at depth 0; verify with a buffer containing `f(g(a, b), c|)` (caret at `|`) → activeParameter = 1 not 3.

---

## Out of scope (Plan 29+)

- `textDocument/references` (lexical analysis across the buffer)
- `textDocument/formatting` (pretty-printer per language)
- Cross-file / cross-module `definition` (import resolution)
- Code actions / quick fixes
- Semantic tokens for richer highlighting
- Rename refactoring
- Compare-view depth (carried since Plan 24)
