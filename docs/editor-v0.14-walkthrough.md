# Phoenixml.Editor v0.14 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-13:** v0.14 ships the Plan 24 carried-features pack — six items that were deferred from Plan 23. Highlights: dual-pane side-by-side compare with synced scrolling and tinted diff rows, drop preview ghost, persistent XPath history across editor restarts, XPath function signature hints, context-aware XPath autocompletion (element / attribute names from the active doc), and namespace-aware (`map:`, `array:`, `xs:`) autocompletion.

## How to run

```bash
cd /raid/elvogel/repos/phoenixml-platform
dotnet run --project src/Phoenixml.Editor --configuration Release -- \
  /raid/tmp/v0.8-smoke/data-and-schemas-by-type/data/library.xml
```

## What's new in v0.14

### Dual-pane compare (Plan 24 #1)

`File → Compare With…` now opens a side-by-side window with two `NodeListView` instances. Rows are tinted by diff status:

- **Added** (right pane only) → muted green
- **Removed** (left pane only) → muted red
- **Changed** (both sides) → muted yellow

Scrolling either pane scrolls the other to the same vertical offset (synced via `ScrollViewer.ScrollChangedEvent`). The header shows the documents being compared and the total difference count.

**Carried limitations** (Plan 25):
- No structural row alignment — each pane renders in its own document order; the diff tint identifies matched nodes by canonical XPath, not visual row position.
- No keyboard navigation between diff entries.
- No in-line editing or merge.

### Drop preview ghost (Plan 24 #2)

While dragging a tree row, a small floating chip displays `Move <element-name>` near the cursor. Implemented as an Avalonia `Popup` with `Placement="Pointer"`. Disappears on drop or cancel.

### Persistent XPath history (Plan 24 #3)

XPath query bar history now persists across editor restarts.

- Stored at `~/.config/phoenixml/xpath-history.txt`, newline-delimited.
- Loaded on startup via `EditorMainWindow` constructor.
- Saved on `Window.Closing`.
- Blank lines are skipped on load.

### XPath function signature hints (Plan 24 #4)

When the caret sits inside the parentheses of a recognized function, a small popup above the input shows its signature. The recognized set is ~35 common XPath 3.1 `fn:*` functions, curated in `XPathSignatures`.

Example: typing `count(` shows `count($arg as item()*) as xs:integer`.

### Context-aware XPath autocompletion (Plan 24 #5)

The completion popup now classifies the caret context:

- **Word immediately after `/` or `//`** → element names harvested from the active document.
- **Word immediately after `@`** (inside a predicate) → attribute names harvested from the active document.
- **Otherwise** → `fn:*` function names.

Routing happens in `XPathContextProvider.DetectContext(text, caret)` which returns `(Kind, Prefix)`. The host supplies a `NamesRequested` callback that delegates to `XPathContextProvider.CollectElementNames` / `CollectAttributeNames`.

### Namespace-aware autocompletion (Plan 24 #6)

Typing `map:`, `array:`, or `xs:` switches the popup to the corresponding namespace bucket. Each bucket is a separate hand-curated list inside `XPathFunctionList`. Unprefixed input continues to filter the `fn:` bucket.

## Smoke-test checklist (manual)

| Action | Expected | Actual |
|---|---|---|
| File → Compare With… → pick a doc | Dual-pane window opens; diff rows tinted; header shows counts. | tbd |
| Scroll the left pane | Right pane scrolls in lockstep. | tbd |
| Drag a tree row | Ghost chip shows `Move <name>` near the cursor; disappears on drop. | tbd |
| Ctrl+G, type `//a`, Enter; close editor; relaunch | Ctrl+G + Up shows `//a`. | tbd |
| Ctrl+G, type `count(`, leave caret after `(` | Signature popup above input shows `count(...)`. | tbd |
| Type `//boo` | Completion popup shows element names starting with `boo` from the active doc. | tbd |
| Type `//book[@isb` | Completion popup shows attributes starting with `isb` from the active doc. | tbd |
| Type `map:` | Popup switches to `map:keys`, `map:get`, etc. | tbd |
| Type `xs:` | Popup switches to `xs:string`, `xs:integer`, etc. | tbd |

## Rough-edges log (v0.15 backlog)

### Resolved 2026-05-13 (Plan 24)

- [x] ~~No dual-pane compare view.~~
- [x] ~~No drop preview ghost.~~
- [x] ~~XPath history doesn't persist across restarts.~~
- [x] ~~No XPath function signature hints.~~
- [x] ~~No context-aware autocompletion (element / attribute names from active doc).~~
- [x] ~~No `map:` / `array:` / `xs:` namespace autocompletion.~~

### High friction (carried to Plan 25)

- [ ] **Structural row alignment in dual-pane compare** — rows in each pane should align across the splitter so matching XPaths sit on the same vertical position.
- [ ] **Keyboard navigation between diffs** (next / previous change).
- [ ] **In-line editing inside compare panes.**
- [ ] **Three-way merge.**
- [ ] **Schema-aware autocompletion** — use the active XSD when present to filter element/attribute completions.

### Medium / low (carried from v0.13)

- [ ] No multi-namespace schema support.
- [ ] No type-aware default values for schema-aware Insert.
- [ ] Documentation popups from `xs:annotation`.
- [ ] Right-click "Apply Schema to..." in the explorer.
- [ ] LSP refresh / editor LSP-client integration.
- [ ] No file-system watcher in the explorer.
- [ ] No node-kind icons.
- [ ] No theme switching.

## Validated environment

- OS: Linux Mint 22.3 (Cinnamon, X11)
- .NET: 10.0
- Avalonia: 11.2.4
- Phoenixml.Platform: 0.14.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.10.0 (in-repo)
- PhoenixmlDb.Core: 1.1.2 (NuGet)
- PhoenixmlDb.XQuery: 1.3.6 (NuGet)
- PhoenixmlDb.Xslt: 1.3.10 (CPM pinned, not referenced)
