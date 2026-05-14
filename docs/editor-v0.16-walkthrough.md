# Phoenixml.Editor v0.16 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-13:** v0.16 wires Plan 25's LSP infrastructure into the editor. Opening a `.xq` or `.xsl` file now: (1) loads it as a `TextDocument`, (2) renders an AvaloniaEdit-based text view in the central pane (replacing the XML tree), (3) spawns the corresponding LSP server, (4) sends `didChange` on every keystroke, and (5) surfaces server diagnostics via the editor's `last-action` status segment.

This is the Theme-A "finish the LSP integration" pack. Plan 25's LSP servers are no longer just unit-tested — they're now driven by the live editor.

## What's new in v0.16

### EditorState generalized to `Document` (Plan 26 #1)

`DocumentTab.Document`, `EditorState.ActiveDocument`, and the `ActiveDocumentChanged` event now operate on the platform base `Document` rather than `XmlDocument`. The text vocabulary (`TextDocument`) rides the same tab system. XML-only consumers (`SchemaResolver`, find bar, XPath bar, breadcrumb, validation pipeline) type-narrow with `is XmlDocument` and degrade gracefully (hide / no-op) for non-XML tabs.

`Document.MarkClean` is now `public` (was `protected`) so dirty-doc handling works uniformly across vocabularies.

`EditorState` gains `TabAdded` / `TabClosed` events used by the LSP wiring.

### AvaloniaEdit `TextEditorView` (Plan 26 #2)

New `Phoenixml.Editor.Views.TextEditorView` UserControl wraps `AvaloniaEdit.TextEditor`. Two-way bound to `TextDocument`:

- Typing into the editor fires `TextDocument.ReplaceAll`.
- External `TextDocument.Changed` (e.g. revert, LSP-driven edits) replaces the editor's buffer.
- A `_suppress` flag prevents feedback loops between the two paths.

Defaults: Cascadia Mono / Consolas monospace, 13pt, line numbers, dark theme.

### Central pane swap (Plan 26 #3)

The central column's content is now a `ContentControl`. `SwapCentralPane(Document?)` picks:

- `NodeListView` for `XmlDocument` (breadcrumb visible, find/xpath bars retain their toggle state)
- `TextEditorView` for `TextDocument` (breadcrumb, find bar, XPath bar all hidden)

### Extension routing (Plan 26 #4)

`OpenFileCommand` and `OpenFileOnLaunchAsync` branch by file extension:

- `.xq`, `.xqy`, `.xquery`, `.xqm`, `.xsl`, `.xslt` → `TextDocument.LoadAsync`
- everything else → `XmlDocument.LoadAsync`

File picker offers separate filter groups for XML, XSLT, XQuery, and All Files.

### LSP server spawn (Plan 26 #5)

- **`LspServerRegistry`** maps `DocumentKind` → on-disk path:
  `AppContext.BaseDirectory/tools/phoenixmldb-xquery-lsp` for `DocumentKind.XQuery`
  `AppContext.BaseDirectory/tools/phoenixmldb-xslt-lsp` for `DocumentKind.Xslt`
  Returns null for unsupported kinds.

- **`LspServerProcess`** wraps `Process.Start` with stdin/stdout piping. `IDisposable` kills the process tree on dispose (best-effort).

### LSP diagnostic bridge (Plan 26 #6)

`LspDiagnosticBridge.Convert` is a pure function: LSP `PublishDiagnosticsParams` → `IReadOnlyList<EditorLspDiagnostic>` (flat record with URI, line, column, length, severity, code, source, message). No host dependencies; trivially testable.

The MVP doesn't yet route LSP diagnostics through the editor's `IDiagnostic` / `ProblemsView` pipeline — that requires the existing `DiagnosticLocation` to accept text-only positions (not bound to a NodeId). For now, diagnostics surface via the editor's `last-action` status segment showing the first message + a `+N more` suffix when the server returns multiple.

### `LspSession` (Plan 26 #7)

Per-document session that couples `TextDocument` ↔ `LspClient` (via either an `LspServerProcess` or arbitrary streams for tests):

- `StartAsync` sends `initialize` + `initialized` + `didOpen`.
- `TextDocument.Changed` → `didChange` with the new buffer + version.
- Server `publishDiagnostics` → `DiagnosticsArrived` event with converted records.
- `DisposeAsync` unsubscribes, disposes the client, and kills the server process.

### EditorMainWindow LSP wiring (Plan 26 #8)

A `Dictionary<DocumentTab, LspSession>` tracks one session per open text doc:

- `TabAdded` for a `TextDocument` → resolve binary via `LspServerRegistry`, spawn `LspServerProcess`, build `LspSession`, call `StartAsync`. If the binary is missing (most common cause: server not published into `AppContext.BaseDirectory/tools/`), `NotifyAction` surfaces the failure without crashing.
- `TabClosed` → dispose the session.
- `DiagnosticsArrived` → first message + count surfaced via `NotifyAction`.

## How to make the LSP servers available to the editor

The server binaries aren't bundled into the editor build today. To enable LSP for `.xq`/`.xsl` files:

```bash
# Publish the XQuery server into the editor's tools/ folder
EDITOR_BIN=/raid/elvogel/repos/phoenixml-platform/src/Phoenixml.Editor/bin/Debug/net10.0
dotnet publish /raid/elvogel/repos/phoenixmldb/phoenixmldb-xquery/src/PhoenixmlDb.XQuery.LanguageServer \
  -c Release -o "$EDITOR_BIN/tools" /p:AssemblyName=phoenixmldb-xquery-lsp
dotnet publish /raid/elvogel/repos/phoenixmldb/phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer \
  -c Release -o "$EDITOR_BIN/tools" /p:AssemblyName=phoenixmldb-xslt-lsp
```

A future plan can wire this as an MSBuild step on the editor's `BeforeBuild`.

## Smoke-test checklist (manual)

| Action | Expected | Actual |
|---|---|---|
| Open an XML file | NodeListView shows; find/xpath bars/breadcrumb work as before. | tbd |
| Open a `.xq` file | TextEditorView opens in central pane; find/xpath bars/breadcrumb hidden. | tbd |
| Type in the text view | `TextDocument.Text` reflects edits; tab title shows dirty marker. | tbd |
| Switch tabs back to XML | Central pane swaps to NodeListView; XML UI re-engages. | tbd |
| Open `.xq` with LSP server published | First text edit produces an LSP diagnostic notification in the status bar. | tbd |
| Open `.xq` with LSP server missing | Status bar: "LSP server not found for XQuery (…)". Editor remains usable. | tbd |
| Save a text doc (Ctrl+S) | Writes back to disk; dirty marker clears. | tbd |

## Carried to Plan 27

- **LSP diagnostics → ProblemsView integration** — currently routed via status bar only. Needs `DiagnosticLocation` to support text positions without a NodeId, plus per-tab diagnostic grouping in `ProblemsView`.
- **LSP hover / completion popups in TextEditorView** — server publishes them; client needs to ask + render.
- **Server crash recovery / restart** — `LspServerProcess` doesn't yet monitor child exits or restart.
- **MSBuild integration** to auto-publish LSP server binaries into `tools/` on editor build.
- **Find/Replace in text view.**
- **Editor-side undo/redo for text edits** (AvaloniaEdit handles internally for now).
- **Syntax highlighting** beyond AvaloniaEdit defaults (custom `IHighlightingDefinition` for XQuery/XSLT).

## Validated environment

- OS: Linux Mint 22.3
- .NET: 10.0
- Avalonia: 11.2.4 / Avalonia.AvaloniaEdit: 11.2.0
- StreamJsonRpc: 2.22.11
- Phoenixml.Platform: 0.16.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.12.0 (in-repo)
- Phoenixml.Platform.Editor.Text: 0.2.0 (in-repo)
- PhoenixmlDb.Core: 1.1.2 / XQuery: 1.3.6 / Xslt: 1.3.10 (NuGet)
- PhoenixmlDb.XQuery.LanguageServer / PhoenixmlDb.Xslt.LanguageServer: in-repo, expected at `tools/`
