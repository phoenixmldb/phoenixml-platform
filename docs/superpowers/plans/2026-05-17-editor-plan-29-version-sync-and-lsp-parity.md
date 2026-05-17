# Editor v0.19 — Plan 29 Engine Version Sync + LSP Parity — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** First, pull phoenixml-platform onto the latest engine releases (PhoenixmlDb.Core 1.1.3, XQuery 1.3.10, XSLT 1.3.14) and verify nothing regressed. Second, close the XSLT LSP capability gap with the XQuery server (`signatureHelp` + `definition`), implement `textDocument/references` across both servers with editor wiring (Shift+F12 + references panel), and finally upgrade the popup placement from cursor-anchored to caret-anchored.

**Architecture:**

- **Version sync (mandatory first task)** — CPM bump in `Directory.Packages.props`, full restore + build + test pass across both engine LSP server repos and the editor. Any analyzer drift or behavior change discovered here is fixed before feature work begins.
- **XSLT `signatureHelp`** — handles `xsl:*` functions and the small set of XSLT-specific function-namespace built-ins (only a few are common; `xsl:type-of`, etc. are rare). Curated table + reuse the parens-walking algorithm from XQuery's handler.
- **XSLT `definition`** — for `<xsl:call-template name="X"/>` find matching `<xsl:template name="X">`; for `$variable` references find matching `<xsl:variable name="X">` or `<xsl:param name="X">`. AST-free regex scan, same approach as `documentSymbol`.
- **`textDocument/references`** — both servers implement it. For variables: find every `$name` token in the buffer. For functions: every `name(` or `prefix:name(` call site. Returns `LspLocation[]`. Editor binds Shift+F12, opens a new `ReferencesView` panel (reuses the OutlineView's dock slot, swapping content) listing locations; clicking a location navigates the caret.
- **Caret-anchored popups** — replace `Placement="Pointer"` with `PlacementMode.AnchorAndGravity` using AvaloniaEdit's `TextArea.TextView.GetVisualPosition(caret, VisualYPosition.LineBottom)` converted to TextEditor-local coordinates. Falls back to Pointer placement if the API path fails.

**Tech Stack:** Avalonia 11.2.4, AvaloniaEdit 11.2.0, StreamJsonRpc, PhoenixmlDb.Core 1.1.3, PhoenixmlDb.XQuery 1.3.10, PhoenixmlDb.Xslt 1.3.14.

**Out of scope (Plan 30+):**
- Semantic tokens (separate LSP capability + protocol surface)
- Cross-module / cross-file `definition`
- Rename refactoring
- Code actions / quick fixes
- Formatting
- Compare-view depth (carried since Plan 24)

---

## File structure

| File | Role |
|---|---|
| `phoenixml-platform/Directory.Packages.props` | Modify — bump Core/XQuery/Xslt versions |
| `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/Handlers/SignatureHelpHandler.cs` | NEW |
| `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/Handlers/DefinitionHandler.cs` | NEW |
| `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/Lsp/LspMessages.cs` | Modify — signature/definition DTOs |
| `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/XsltLanguageServer.cs` | Modify — handlers + capabilities |
| `phoenixmldb-xquery/src/PhoenixmlDb.XQuery.LanguageServer/Handlers/ReferencesHandler.cs` | NEW |
| `phoenixmldb-xquery/src/PhoenixmlDb.XQuery.LanguageServer/XQueryLanguageServer.cs` | Modify — `textDocument/references` |
| `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/Handlers/ReferencesHandler.cs` | NEW |
| `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/XsltLanguageServer.cs` | Modify — `textDocument/references` |
| `phoenixml-platform/src/Phoenixml.Editor/Lsp/LspClient.cs` | Modify — `RequestReferencesAsync` |
| `phoenixml-platform/src/Phoenixml.Editor/Lsp/LspSession.cs` | Modify — `RequestReferencesAsync` |
| `phoenixml-platform/src/Phoenixml.Editor/Lsp/LspMessages.cs` | Modify — `LspReferenceParams` |
| `phoenixml-platform/src/Phoenixml.Editor/Views/ReferencesView.axaml(.cs)` | NEW — list of references; click navigates |
| `phoenixml-platform/src/Phoenixml.Editor/Views/TextEditorView.axaml(.cs)` | Modify — Shift+F12 binding; caret-anchored popups |
| `phoenixml-platform/src/Phoenixml.Editor/EditorMainWindow.cs` | Modify — host ReferencesView |
| `docs/editor-v0.19-walkthrough.md` | NEW |

Tests:
- `phoenixmldb-xslt/tests/PhoenixmlDb.Xslt.LanguageServer.Tests/SignatureHelpTests.cs` (3 tests)
- `phoenixmldb-xslt/tests/PhoenixmlDb.Xslt.LanguageServer.Tests/DefinitionTests.cs` (4 tests)
- `phoenixmldb-xquery/tests/PhoenixmlDb.XQuery.LanguageServer.Tests/ReferencesTests.cs` (4 tests)
- `phoenixmldb-xslt/tests/PhoenixmlDb.Xslt.LanguageServer.Tests/ReferencesTests.cs` (3 tests)

---

## Tasks

### Task 1: Engine version sync

**Files:**
- Modify: `phoenixml-platform/Directory.Packages.props`

- [ ] **Step 1: Bump CPM pins**

```xml
<PackageVersion Include="PhoenixmlDb.Core" Version="1.1.3" />
<PackageVersion Include="PhoenixmlDb.XQuery" Version="1.3.10" />
<PackageVersion Include="PhoenixmlDb.Xslt" Version="1.3.14" />
```

- [ ] **Step 2: Read engine RELEASES.md for breaking changes**

Quickly scan:
```
cat /raid/elvogel/repos/phoenixmldb/phoenixmldb-xquery/RELEASES.md | head -100
cat /raid/elvogel/repos/phoenixmldb/phoenixmldb-xslt/RELEASES.md | head -100
```

If any new APIs are introduced that the editor or LSP servers should consume immediately (e.g., new `SourceLocation` properties for D7 site population), note them — but defer actual consumption unless trivial.

- [ ] **Step 3: Restore + build**

Run:
```bash
cd /raid/elvogel/repos/phoenixml-platform
dotnet restore
dotnet build --nologo
```

Expected: 0 errors. If analyzers flag anything new, address by either suppressing in the appropriate csproj's `<NoWarn>` or by fixing the underlying code.

- [ ] **Step 4: Full test pass**

```bash
dotnet test --nologo
```

Expected: all suites green. If any test breaks due to a behavior change in the engines, investigate before proceeding.

- [ ] **Step 5: Commit the version bump alone**

Per the `feedback_phoenixmldb_version_cadence` memory note: bump in its own commit so it can be cleanly reverted if a regression surfaces.

```bash
git add Directory.Packages.props
git commit -m "chore: bump PhoenixmlDb.Core 1.1.3, XQuery 1.3.10, Xslt 1.3.14"
```

---

### Task 2: XSLT `signatureHelp`

**Files:**
- Modify: `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/Lsp/LspMessages.cs`
- Create: `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/Handlers/SignatureHelpHandler.cs`
- Modify: `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/XsltLanguageServer.cs`
- Create: `phoenixmldb-xslt/tests/PhoenixmlDb.Xslt.LanguageServer.Tests/SignatureHelpTests.cs`

**Approach:** Mirror the XQuery server's handler. Same parens-walking algorithm, smaller signature table (XSLT-specific functions are rarer). For MVP: cover `current()`, `system-property()`, `unparsed-text()`, `document()`, `format-number()`, plus the same `fn:*` set as XQuery.

- [ ] **Step 1: Add DTOs to LspMessages.cs**

```csharp
public sealed record TextDocumentPositionParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("position")] Position Position);

public sealed record SignatureInformation(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("documentation")] string? Documentation);

public sealed record SignatureHelp(
    [property: JsonPropertyName("signatures")] SignatureInformation[] Signatures,
    [property: JsonPropertyName("activeSignature")] int ActiveSignature,
    [property: JsonPropertyName("activeParameter")] int ActiveParameter);

public sealed record SignatureHelpOptions(
    [property: JsonPropertyName("triggerCharacters")] string[] TriggerCharacters);
```

Add `SignatureHelpProvider` property to `ServerCapabilities`.

- [ ] **Step 2: Implement `SignatureHelpHandler`**

Copy structure from XQuery handler. Signature table includes ~10 XSLT-specific entries plus the common fn:* set. Strip `fn:` / `xsl:` / `xs:` prefixes before lookup.

- [ ] **Step 3: Wire JSON-RPC method + capability**

- [ ] **Step 4: Write 3 tests**
- Outside function call → null
- `count(` → returns count signature
- Unknown function → null

- [ ] **Step 5: Run**

---

### Task 3: XSLT `definition`

**Files:**
- Modify: `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/Lsp/LspMessages.cs`
- Create: `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/Handlers/DefinitionHandler.cs`
- Modify: `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/XsltLanguageServer.cs`
- Create: `phoenixmldb-xslt/tests/PhoenixmlDb.Xslt.LanguageServer.Tests/DefinitionTests.cs`

**Approach:** Token at caret:

- `$varname` → match `<xsl:variable name="varname"` or `<xsl:param name="varname"`
- bare identifier → match `<xsl:template name="identifier"` (call-template target)

Return the `name="..."` attribute's value range.

- [ ] **Step 1: Add `Location` DTO**

```csharp
public sealed record Location(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("range")] Range Range);
```

Add `DefinitionProvider` to `ServerCapabilities`.

- [ ] **Step 2: Implement handler**

```csharp
public static class DefinitionHandler
{
    public static Location? Handle(DocumentBuffer buf, Position pos)
    {
        var offset = PositionToOffset(buf.Text, pos);
        var token = ExtractTokenAt(buf.Text, offset);
        if (token is null) return null;
        var pattern = token.Value.IsVariable
            ? new Regex(@"<xsl:(?:variable|param)\s+(?:[^>]*?\s)?name\s*=\s*[""'](" + Regex.Escape(token.Value.Name) + @")[""']")
            : new Regex(@"<xsl:template\s+(?:[^>]*?\s)?name\s*=\s*[""'](" + Regex.Escape(token.Value.Name) + @")[""']");
        var match = pattern.Match(buf.Text);
        if (!match.Success) return null;
        var declNameGroup = match.Groups[1];
        var startPos = OffsetToPosition(buf.Text, declNameGroup.Index);
        var endPos = OffsetToPosition(buf.Text, declNameGroup.Index + declNameGroup.Length);
        return new Location(buf.Uri, new Range(startPos, endPos));
    }
    // helpers identical to XQuery's
}
```

- [ ] **Step 3: 4 tests**
- Variable reference finds `<xsl:variable>`
- Variable reference finds `<xsl:param>`
- Bare name finds `<xsl:template name>`
- Unknown returns null

- [ ] **Step 4: Run**

---

### Task 4: XQuery `textDocument/references`

**Files:**
- Modify: `phoenixmldb-xquery/src/PhoenixmlDb.XQuery.LanguageServer/Lsp/LspMessages.cs`
- Create: `phoenixmldb-xquery/src/PhoenixmlDb.XQuery.LanguageServer/Handlers/ReferencesHandler.cs`
- Modify: `phoenixmldb-xquery/src/PhoenixmlDb.XQuery.LanguageServer/XQueryLanguageServer.cs`
- Create: `phoenixmldb-xquery/tests/PhoenixmlDb.XQuery.LanguageServer.Tests/ReferencesTests.cs`

**Approach:** Token at caret. Regex scan buffer for matches; emit `Location[]` covering each occurrence's range.

- `$name` → all `\$name\b`
- function name → all `(?<!\w)name\s*\(` (with optional `prefix:` prefix)

The declaration site itself is included.

- [ ] **Step 1: Add `ReferenceParams` DTO**

```csharp
public sealed record ReferenceParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("position")] Position Position,
    [property: JsonPropertyName("context")] ReferenceContext? Context);

public sealed record ReferenceContext(
    [property: JsonPropertyName("includeDeclaration")] bool IncludeDeclaration);
```

Add `ReferencesProvider` to `ServerCapabilities`.

- [ ] **Step 2: Implement handler**

Token extraction matches the definition handler's. Match scan over the buffer; convert each match's index/length to LSP `Range`.

- [ ] **Step 3: Wire JSON-RPC method**

```csharp
[JsonRpcMethod("textDocument/references")]
public Lsp.Location[] References(ReferenceParams p)
{
    if (!_buffers.TryGetValue(p.TextDocument.Uri, out var buf)) return Array.Empty<Lsp.Location>();
    return Handlers.ReferencesHandler.Handle(buf, p.Position);
}
```

- [ ] **Step 4: 4 tests**
- Variable: finds all `$x` occurrences (decl + uses)
- Function: finds all `local:foo()` call sites + the decl
- Unknown token → empty array
- Match counts respect word boundaries (`$x` doesn't match inside `$xy`)

- [ ] **Step 5: Run**

---

### Task 5: XSLT `textDocument/references`

**Files:**
- Modify: `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/Lsp/LspMessages.cs`
- Create: `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/Handlers/ReferencesHandler.cs`
- Modify: `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/XsltLanguageServer.cs`
- Create: `phoenixmldb-xslt/tests/PhoenixmlDb.Xslt.LanguageServer.Tests/ReferencesTests.cs`

**Approach:** Token at caret. For `$varname`: scan `\$varname\b`. For template names: scan `call-template\s+name\s*=\s*["']name["']` plus the declaration's own `<xsl:template name="...">`.

- [ ] **Step 1: DTOs (same as XQuery's)**
- [ ] **Step 2: Implement handler**
- [ ] **Step 3: Wire JSON-RPC method**
- [ ] **Step 4: 3 tests** — variable refs, template-name refs, unknown returns empty
- [ ] **Step 5: Run**

---

### Task 6: Editor `RequestReferencesAsync` + Shift+F12 + ReferencesView

**Files:**
- Modify: `phoenixml-platform/src/Phoenixml.Editor/Lsp/LspMessages.cs`
- Modify: `phoenixml-platform/src/Phoenixml.Editor/Lsp/LspClient.cs`
- Modify: `phoenixml-platform/src/Phoenixml.Editor/Lsp/LspSession.cs`
- Create: `phoenixml-platform/src/Phoenixml.Editor/Views/ReferencesView.axaml(.cs)`
- Modify: `phoenixml-platform/src/Phoenixml.Editor/Views/TextEditorView.axaml.cs` (Shift+F12)
- Modify: `phoenixml-platform/src/Phoenixml.Editor/EditorMainWindow.cs` (host ReferencesView)

- [ ] **Step 1: DTOs in editor's LspMessages.cs**

```csharp
public sealed record LspReferenceContext(
    [property: JsonPropertyName("includeDeclaration")] bool IncludeDeclaration);

public sealed record LspReferenceParams(
    [property: JsonPropertyName("textDocument")] LspTextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("position")] LspPosition Position,
    [property: JsonPropertyName("context")] LspReferenceContext? Context);
```

- [ ] **Step 2: `LspClient.RequestReferencesAsync`**

```csharp
public Task<LspLocation[]?> RequestReferencesAsync(string uri, int line, int character) =>
    _rpc.InvokeAsync<LspLocation[]?>("textDocument/references",
        new LspReferenceParams(
            new LspTextDocumentIdentifier(uri),
            new LspPosition(line, character),
            new LspReferenceContext(IncludeDeclaration: true)));
```

- [ ] **Step 3: `LspSession.RequestReferencesAsync` wrapper**

- [ ] **Step 4: `ReferencesView.axaml(.cs)`**

A `ListBox` showing one row per location: `Line N — preview`. Selection raises `LocationActivated` event with the `LspLocation`. Header shows total count.

- [ ] **Step 5: TextEditorView Shift+F12 binding**

```csharp
if (e.Key == Key.F12 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
{
    await ShowReferencesAsync();
    e.Handled = true;
}
```

`ShowReferencesAsync` asks the session, raises a new `ReferencesRequested` event with the result for EditorMainWindow to render in the side panel.

- [ ] **Step 6: EditorMainWindow hosts ReferencesView in the same dock slot as OutlineView**

Use a `ContentControl` for the right rail; toggle between Outline and References content. Default: Outline. Shift+F12 switches to References; pressing Escape (or clicking a result) switches back to Outline.

- [ ] **Step 7: Smoke**

---

### Task 7: Caret-anchored popups

**Files:**
- Modify: `phoenixml-platform/src/Phoenixml.Editor/Views/TextEditorView.axaml.cs`

**Approach:** Replace `Placement="Pointer"` on completion/hover/signature popups with absolute positioning anchored at the caret pixel. `TextEditor.TextArea.TextView.GetVisualPosition(caret, VisualYPosition.LineBottom)` returns the caret's text-view-relative point; subtracting `ScrollOffset` gives editor-control-relative; we set this as the popup's `HorizontalOffset`/`VerticalOffset` with `Placement="AnchorAndGravity"`.

- [ ] **Step 1: Add `PositionPopupAtCaret(Popup)` helper**

```csharp
private void PositionPopupAtCaret(Popup popup)
{
    var editor = this.FindControl<TextEditor>("Editor")!;
    var caret = editor.TextArea.Caret;
    try
    {
        var visualPos = editor.TextArea.TextView.GetVisualPosition(
            caret.Position, AvaloniaEdit.Rendering.VisualYPosition.LineBottom);
        var screenPos = visualPos - editor.TextArea.TextView.ScrollOffset;
        popup.HorizontalOffset = screenPos.X;
        popup.VerticalOffset = screenPos.Y;
        popup.Placement = PlacementMode.AnchorAndGravity;
        popup.PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft;
        popup.PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight;
    }
    catch (Exception)
    {
        // AvaloniaEdit API drift — fall back to Pointer placement (Plan 28 behaviour)
        popup.Placement = PlacementMode.Pointer;
    }
}
```

- [ ] **Step 2: Call before opening each popup**

In `ShowCompletionAsync`, `ShowHoverAfterDelayAsync`, `ShowSignatureHelpAsync`:

```csharp
PositionPopupAtCaret(popup);
popup.IsOpen = true;
```

- [ ] **Step 3: Smoke** — no unit tests for UI placement; manual verification only

---

### Task 8: Walkthrough + per-repo commits

**Files:**
- Create: `phoenixml-platform/docs/editor-v0.19-walkthrough.md`

- [ ] **Step 1: Walkthrough**
- [ ] **Step 2: Full test pass across all 3 repos**
- [ ] **Step 3: Per-repo commits**
  - `phoenixmldb-xquery`: references handler + tests
  - `phoenixmldb-xslt`: signatureHelp + definition + references handlers + tests
  - `phoenixml-platform`: CPM bump (already committed in Task 1), client/session API extensions, ReferencesView, Shift+F12, caret-anchored popups, walkthrough

---

## Risk register

- **Engine behavior changes between 1.3.6 → 1.3.10 (XQuery)** and **1.3.10 → 1.3.14 (XSLT)** — these are multiple releases. Most likely AST-shape stability is preserved, but test failures during Task 1 are possible. Investigate before continuing.
- **AvaloniaEdit `GetVisualPosition` API drift** — the method exists in 11.2.0 but the exact signature (does it take a `TextLocation` or a `TextViewPosition`?) needs verification on first build. Task 7 step 1 includes a try/catch fallback to Pointer placement.
- **`textDocument/references` performance** — for a 5000-line buffer, regex-scanning every occurrence is O(n) per request. Fine for MVP; would benefit from indexed lookup at the AST level for large files.
- **XSLT `signatureHelp` value** — XSLT users rarely use function-call syntax outside attribute values (`select="..."`). The signature popup may rarely fire in practice; still worth shipping for parity.
- **References panel UX collision with Outline** — both want the same right-rail dock slot. Plan trades them via `ContentControl`; the toggle UX needs manual smoke.
