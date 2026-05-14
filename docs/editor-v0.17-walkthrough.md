# Phoenixml.Editor v0.17 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-14:** v0.17 turns Plan 26's working-but-rough text editor + LSP integration into something that feels like a real editor. Diagnostics flow into the Problems pane; XQuery files render with syntax highlighting; Ctrl+Space pops completion; hovering for ~500ms pops definitions; Ctrl+Z bridges to AvaloniaEdit's undo; LSP server binaries auto-publish into the editor's `tools/` folder on build.

## What's new in v0.17

### `DiagnosticLocation` refactor (Plan 27 #1)

`DiagnosticLocation` now accepts a nullable `NodeId` plus optional `Length` and `Uri`. XML diagnostics keep the NodeId; LSP-driven text diagnostics carry the URI + (Line, Column) so the ProblemsView can group by file and route click-to-navigate correctly. Existing call sites are unchanged (positional constructor).

### LSP diagnostics → ProblemsView (Plan 27 #2)

New `LspProblemsBridge` converts LSP `PublishDiagnosticsParams` into editor `IDiagnostic` records. `LspSession` exposes both `DiagnosticsArrived` (the flat editor records) and the new `RawDiagnosticsArrived` (raw LSP payload for bridge consumption).

`EditorMainWindow` subscribes to the raw stream and routes through:

- `_diagIndex.Reset(converted)` — tree rows get error glyphs (XML side)
- `_problemsView.SetDiagnostics(converted)` — Problems pane shows per-file diagnostics
- Click-to-navigate: if the location has a `NodeId` → tree select; if it has a `Uri` → switch tabs (caret-move TBD in a follow-up)

### Syntax highlighting (Plan 27 #3)

- **XQuery**: custom `XQuery.xshd` embedded in the editor assembly. Covers keywords (declare/function/let/for/where/return/typeswitch/...), `$variables`, numbers, strings (single and double), `(: comments :)`, function-call sites, and operators.
- **XSLT**: reuses AvaloniaEdit's built-in XML highlighting.
- `HighlightingRegistry` caches definitions by `DocumentKind`. Result cached per registry instance.

### LSP completion popup (Plan 27 #4)

In a text view: **Ctrl+Space** asks the LSP server for completions. Results appear in a popup at the cursor (Pointer placement for MVP); Up/Down navigate, Tab/Enter accept the selection (replacing the word at the caret), Escape closes. The word boundary covers letters, digits, `-`, `_`, `:`, and `$` so `xs:string` and `$variable` complete cleanly.

### LSP hover popup (Plan 27 #5)

Caret movement starts a 500ms debounce. When the user pauses, the LSP server is asked for hover info at the caret position. Non-empty responses render in a non-hit-test-visible popup near the cursor. Moving the caret cancels the request before it fires (`CancellationTokenSource`).

### Find/Replace logic for text (Plan 27 #6 — partial)

New `TextFindController` provides pure find/replace logic over plain text: `Find(text, query, matchCase) → list of (offset, length)`, `ReplaceAll`, `ReplaceSingle`. UI wiring (FindBar routing for text docs + AvaloniaEdit selection-range highlighting) is **deferred to Plan 28** — the logic is in place and unit-tested; the UI plumbing is its own task.

### Undo/Redo bridging (Plan 27 #7)

Ctrl+Z and Ctrl+Y now branch on the active document kind:

- `XmlDocument` → existing `UndoCommand` / `RedoCommand` (command-based undo)
- `TextDocument` → `TextEditorView.Undo()` / `Redo()` → AvaloniaEdit's internal undo stack

### LSP server crash recovery (Plan 27 #7)

`LspServerProcess` now raises `Exited` when the child process terminates. `EditorMainWindow` catches it and surfaces a `NotifyAction` advising the user to close and reopen the tab to restart. Automatic restart-with-backoff is a Plan 28 candidate (preventing crash loops requires a retry counter + timing).

### MSBuild auto-publish for LSP servers (Plan 27 #8)

Editor build now publishes both LSP server projects into `bin/Debug/net10.0/tools/` automatically — no manual `dotnet publish` step required. The target is conditional on the sibling repos being checked out at `../../../phoenixmldb/phoenixmldb-{xquery,xslt}/...` and on the published binary not already existing (incremental builds stay fast). `ContinueOnError="true"` keeps the editor build green even if the LSP publish fails.

## Smoke-test checklist (manual)

| Action | Expected | Actual |
|---|---|---|
| Open `.xq` file | Text view with syntax-highlighted XQuery keywords + variables. | tbd |
| Open `.xsl` file | Text view with XML syntax highlighting (Avalonia built-in). | tbd |
| Edit `.xq` to introduce a parse error | Problems pane shows the error with file path. | tbd |
| Fix the parse error | Problems pane clears. | tbd |
| Double-click a Problems entry for a text diagnostic | Editor switches to the matching tab. | tbd |
| Ctrl+Space in `.xq` | Completion popup with `count`, `string`, `concat`, … | tbd |
| Tab to accept a completion item | Word at caret is replaced with the chosen completion. | tbd |
| Pause cursor on a function name | Hover popup appears after ~500ms (when server provides info). | tbd |
| Ctrl+Z after editing text | Last edit is undone via AvaloniaEdit's undo stack. | tbd |
| Kill the LSP server process externally | Status bar: "LSP server exited unexpectedly (XQuery) — close + reopen tab to restart". | tbd |
| `dotnet build` editor | `bin/Debug/net10.0/tools/phoenixmldb-xquery-lsp` + `phoenixmldb-xslt-lsp` present. | tbd |

## Carried to Plan 28

- **Find/Replace UI wiring** for text view (controller exists; FindBar routing + selection-range highlighting deferred)
- **Hover/completion popup placement at the caret** rather than cursor — needs AvaloniaEdit's caret-to-screen helper
- **LSP server restart with backoff** — currently surfaces the crash but doesn't auto-restart
- **LSP definition / references / formatting / code actions / signature help** — server-side work + client wiring
- **Compare-view depth** — carried since Plan 24
- **Shared `PhoenixmlDb.Lsp.Common` library** — both servers + editor client still duplicate DTOs

## Validated environment

- OS: Linux Mint 22.3
- .NET: 10.0
- Avalonia: 11.2.4 / Avalonia.AvaloniaEdit: 11.2.0
- StreamJsonRpc: 2.22.11
- Phoenixml.Platform: 0.17.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.13.0 (in-repo)
- Phoenixml.Platform.Editor.Text: 0.3.0 (in-repo)
- PhoenixmlDb.Core: 1.1.2 / XQuery: 1.3.6 / Xslt: 1.3.10 (NuGet)
- PhoenixmlDb.XQuery.LanguageServer / PhoenixmlDb.Xslt.LanguageServer: in-repo, auto-published to `tools/`
