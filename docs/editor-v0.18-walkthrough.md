# Phoenixml.Editor v0.18 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-14:** v0.18 extends the LSP infrastructure with three new server capabilities (`documentSymbol`, `signatureHelp`, `definition`) and wires three corresponding editor experiences: outline view, signature popup, F12 navigation. Plus: AvaloniaEdit's built-in search panel for Find in text view, and LSP server auto-restart with exponential backoff.

## What's new in v0.18

### `documentSymbol` (XQuery + XSLT servers + editor)

Both LSP servers now implement `textDocument/documentSymbol`:

- **XQuery** walks the AST's `ModuleExpression.Declarations` collecting `FunctionDeclarationExpression`, `VariableDeclarationExpression`, `NamespaceDeclarationExpression`, and `ModuleImportExpression` nodes. Parse failures produce an empty list.
- **XSLT** regex-scans the buffer for `<xsl:template>`, `<xsl:variable>`, `<xsl:param>`, and `<xsl:function>` declarations. AST-free so incomplete stylesheets still produce useful symbols.

**Editor outline view** (`OutlineView` UserControl) docks 220 dip wide to the right of the central pane when a text doc is active. Lists symbols by name; selection triggers `MoveCaretTo(symbol.Range.Start)` in `TextEditorView`. Refreshes on every `publishDiagnostics` (cheap RPC).

### `signatureHelp` (XQuery server + editor)

Server-side handler walks back from the caret to find the nearest unmatched `(`, reads the function name before it, looks up the signature in a curated table of ~35 common `fn:*` signatures, counts top-level commas for `activeParameter`.

Editor: typing `(` triggers a `Task.Yield()` (let the paren insert) then a `RequestSignatureHelpAsync` call. Result renders in a popup above the caret.

### `definition` (XQuery server + editor)

Server-side handler extracts the token at the caret and regex-scans the buffer:

- `$variable` → `(?:declare\s+variable|for|let)\s+\$varname\b`
- function name → `declare\s+function\s+(?:prefix:)?fnname\s*\(`

Returns an `LspLocation` pointing at the declaration name token. Cross-module resolution is deferred to Plan 29.

**Editor F12** binding in `TextEditorView`: ask server for definition at caret, `MoveCaretTo` the result.

### AvaloniaEdit search panel (text-view Find)

Instead of bridging the editor's existing `FindBar` to text-buffer search, v0.18 enables AvaloniaEdit's built-in `SearchPanel.Install(editor)`. Ctrl+F inside a text view shows AvaloniaEdit's native search bar with prev/next, case-sensitive toggle, regex, whole-word. Battle-tested; integrates with AvaloniaEdit's selection rendering for free.

### LSP server auto-restart with backoff

`LspServerProcess.Exited` triggers a retry loop in `EditorMainWindow`:

- Attempts: 3
- Backoff: 1s, 3s, 9s (`3^(attempt-1)` seconds)
- Per-tab counter in `_lspRestartAttempts`
- Counter resets to 0 on the next successful `publishDiagnostics` (we assume that means the server initialized and is healthy)
- After 3 failures: `NotifyAction` with give-up message; the user can manually close + reopen the tab to retry

Stale session is disposed before the new spawn — this avoids two parallel sessions for the same doc.

### Carried to Plan 29

- **`textDocument/references`** — find all uses of a symbol
- **`textDocument/formatting`** — pretty-printer per language
- **Cross-file / cross-module `definition`** — follow imports
- **Caret-anchored popups** — currently `Placement="Pointer"` for completion/hover/signature; ideal is anchored at the actual caret pixel coords
- **XSLT `signatureHelp` / `definition`** — XSLT server only implements `documentSymbol` so far
- **Code actions / quick fixes**
- **Semantic tokens** for richer highlighting
- **Rename refactoring**
- **Compare-view depth** (carried since Plan 24)

## Smoke-test checklist (manual)

| Action | Expected | Actual |
|---|---|---|
| Open `.xq` with `declare function local:foo() ...` | Outline view shows `foo` (Function symbol). | tbd |
| Click `foo` in outline | Caret moves to the `foo` token in source. | tbd |
| Open `.xsl` with `<xsl:template match="/book">...` | Outline shows `/book` (Method symbol). | tbd |
| In `.xq`, type `count(` | Popup above input shows `count($arg as item()*) as xs:integer`. | tbd |
| In `.xq`, position cursor on a `$variable` reference, press F12 | Caret jumps to the `declare variable $name` declaration. | tbd |
| In `.xq`, position cursor on a `local:foo()` call, press F12 | Caret jumps to the `declare function local:foo` declaration. | tbd |
| In a text view, press Ctrl+F | AvaloniaEdit's built-in search bar appears with prev/next/case/regex controls. | tbd |
| Kill the LSP child process externally | Editor shows "LSP exited — restart 1/3 in 1s", then "2/3 in 3s", … then gives up after 3. | tbd |
| Server initializes successfully after a crash | Restart counter resets — next crash gets full 3-attempt budget. | tbd |

## Validated environment

- OS: Linux Mint 22.3
- .NET: 10.0
- Avalonia: 11.2.4 / Avalonia.AvaloniaEdit: 11.2.0
- StreamJsonRpc: 2.22.11
- Phoenixml.Platform: 0.18.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.14.0 (in-repo)
- Phoenixml.Platform.Editor.Text: 0.4.0 (in-repo)
- PhoenixmlDb.Core: 1.1.2 / XQuery: 1.3.6 / Xslt: 1.3.10 (NuGet)
