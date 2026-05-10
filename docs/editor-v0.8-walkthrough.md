# Phoenixml.Editor v0.8 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-09:** v0.8 ships **two threads of work**:
1. **Schema management UX** (Plan 14) — opinionated auto-detection across folder layouts, schema cache, GridSplitter for the explorer column, "Set Schema…" file picker, reload-from-disk on Discard.
2. **Find in tree** (Plan 15) — first XML Notepad parity feature. Ctrl+F embedded find bar, three-state row tint (None / Match / ActiveMatch), live debounced search with cycle navigation.

Plus a strategic clarification about the framework choice and a deferred LSP integration cycle (engine-side release cadence dependency).

## How to run

```bash
cd /raid/elvogel/repos/phoenixml-platform
dotnet run --project src/Phoenixml.Editor --configuration Release -- /path/to/file.xml
```

For the v0.8 demo (auto-detect + Find), use the three-layout fixture at `/raid/tmp/v0.8-smoke/`:

```
v0.8-smoke/
├── data-and-schemas-mixed/      # Layout A: name-matched siblings
├── data-and-schemas-by-type/    # Layout B: data/ + schemas/ siblings
└── data-with-nested-schemas/    # Layout C: schemas/ subfolder
```

## What works in v0.8

### Schema management (Plan 14)

- **`SchemaCache`** keyed by `(absolute path, last-modified-utc ticks)`. External XSD edits picked up via timestamp; no `FileSystemWatcher` needed. Eliminates per-validation XSD re-reads.
- **`SchemaResolver` opinionated auto-detect**, runs after persisted override and `xsi:` hints, before giving up. Six priority levels:
  1. `<dir>/<base>.xsd` (same-folder name match)
  2. `<dir>/*.xsd` (same folder, exactly one)
  3. `<dir>/schemas/<base>.xsd` or `<dir>/xsd/<base>.xsd`
  4. `<dir>/schemas/*.xsd` or `<dir>/xsd/*.xsd`, exactly one
  5. `<dir>/../schemas/<base>.xsd` or `<dir>/../xsd/<base>.xsd` (the `data/ + schemas/` layout)
  6. `<dir>/../schemas/*.xsd` or `<dir>/../xsd/*.xsd`, exactly one
  Multiple unrelated XSDs without a name match → don't gamble; return null. Documents without a `Source` URI skip auto-detect.
- **Smart explorer auto-root** — if the file's parent has a sibling `schemas/` or `xsd/` folder, the explorer roots one level up so both folders are visible. Otherwise roots at the file's own folder. Auto-expands the root TreeViewItem on open so the user sees folder contents immediately.
- **`EditorSettings.PerDocumentSchema`** override map keyed by document path; survives editor restart. Settable via the File-menu picker.
- **GridSplitter** between explorer and node list, persisted width via `EditorSettings.ExplorerWidth`.
- **File → Set Schema…** picker as the manual fallback; opens an `.xsd` chooser, persists the choice, immediately revalidates.
- **Discard reloads from disk** — replaces Plan 12's `MarkClean` lie with re-parse + `EditorState.ReplaceDocument`. Never-saved docs still fall back to `MarkClean` since there's nothing to reload from.

### Find in tree (Plan 15) — NEW

- **Ctrl+F** opens an embedded find bar above the active document's tree.
- **Live debounced search** (200ms) — type to filter, no Enter required.
- **Searches** element local names, attribute local names, attribute values, and text node content.
- **Three-state row tint:**
  - **ActiveMatch** = solid blue (currently navigated)
  - **Match** = subtle yellow (one of N matches)
  - **None** = transparent
- **Enter / Shift+Enter** cycle forward / backward through matches; wraps at endpoints.
- **EnsureVisibleAndSelect** expands ancestors of the active match — works even on collapsed trees.
- **Match counter** ("3 / 17", or "no matches" when query has no hits).
- **Match-case toggle** (Aa) — defaults to off (case-insensitive); click to enable case-sensitive.
- **Escape** closes the bar and clears highlights.
- Architecture mirrors Plan 13's `DiagnosticIndex` — `MatchIndex` (NodeId → state), `IMatchIndex` interface, `XmlTreeNodeProvider.MatchIndex` settable property, refresh via `provider.TriggerRefresh()`.

### What's deferred from v0.8

- **Right-click "Apply Schema to..." in the explorer** — committed but unverified. Three iterations against Avalonia's TreeView ContextMenu didn't yield a working flow; instrumentation confirmed the detection chain works perfectly but the menu-display mechanism is fragile in our setup. Auto-detect covers ~90% of the workflow this gesture would have served, so the gesture is parked. To revisit in a focused Plan 16+ spike with proper Avalonia investigation upfront.
- **LSP refresh** — investigated, deferred. The `PhoenixmlDb.LanguageServer` (in `phoenixmldb/devtools/`) currently can't build because of a release-cascade issue: ConduitXP.Steps.Standard requires `PhoenixmlDb.XQuery 1.3.2`, but only `1.3.1` is on NuGet — and the local XQuery source has uncommitted code by the engine agents bumping toward 1.3.2. Trying to refresh the LSP now would step on in-flight engine work. Revisit once the XQuery 1.3.2 / Xslt 1.3.3 release cascade settles.

## Bugs caught and fixed during v0.8

1. **Empty-menu bug from `_tree.ContextMenu = m` + populate-on-ContextRequested** — Avalonia 11 didn't reliably render items added during the event. Diagnostic logging surfaced this after three iterations of guesswork. The patterns that DON'T work in Avalonia 11.2.4 with TreeView:
   - Per-row ContextMenu attached to a StackPanel without `Background = Transparent` (no hit-test).
   - Tree-level ContextMenu + visual-tree-walk in `Opening` (PlacementTarget unreliable).
   - Tree-level ContextMenu + populate items in `ContextRequested` (items added late don't render).
   Imperative `menu.Open(tvi)` with `Placement = Pointer` from inside `ContextRequested` was the fourth attempt; committed `wip` since not visually verified yet.
2. **`UpdateExplorerWidth` race** with concurrent saves — fixed by SemaphoreSlim around `SaveAsync` so fire-and-forget calls (from drag) don't collide with explicit awaited calls (from tests).
3. **Avalonia `TreeView` re-collapse on double-click** — handled by setting `e.Handled = true` for both folder and file double-clicks in the explorer.
4. **Explorer auto-root narrowed to file's parent** when the user obviously needed the grandparent visible (data/ + schemas/ siblings). Smart-root walks up one level when a `schemas/`/`xsd/` sibling folder exists.

## Architectural validations

- **Opinionated heuristics over modal dialogs.** The Plan 14 auto-detect cascade reduced "open a file → see it validate" from 4 clicks to 0 clicks for the common layouts. Once the heuristic was tuned, the smart-root + auto-expand made the explorer state arrive ready-to-use without any further interaction. This validates the editor design philosophy memory we wrote (`feedback_lazy_developers_opinionated.md`): opinionated first, manual fallback second, every gesture saved is a gesture not paid for.
- **Plan 13's `DiagnosticIndex` pattern generalizes.** `MatchIndex` reused the same surface (interface, settable property on provider, `TriggerRefresh` for refresh). Confidence that future overlays (e.g., search-results-from-LSP, find-references) will plug into the same shape.
- **Avalonia is fine for XML Notepad parity, with caveats.** Most parity features (text inputs, popups, dialogs, keyboard nav, drag/drop, splitters, simple tree views) sit comfortably in Avalonia's grain. The TreeView-with-ContextMenu rough patch is one outlier, not a representative sample. Right-click defer doesn't change the framework verdict.
- **Cross-repo coordination matters.** Plan 13's NuGet release of Core 1.1.0 worked smoothly because the release script handled it; Plan 14 LSP refresh was scoped naively because we didn't audit cross-repo state first. Audit before scoping is now the rule for anything that touches engines.

## Smoke-test results (2026-05-09)

Loaded `/raid/tmp/v0.8-smoke/data-and-schemas-by-type/data/library.xml`. Editor opens with the explorer rooted at `data-and-schemas-by-type/` (smart-root walked up to include the sibling `schemas/`), explorer expanded to show both folders, and the file's two known schema errors highlighted with inline ✕ glyphs.

| Action | Result |
|---|---|
| Open file | Smart-root, auto-expand, auto-detect schema (Layout B), validation passes through with the 2 expected errors. |
| Edit file | Validation re-runs after debounce; glyphs update. |
| Drag splitter | Explorer width changes; persists across relaunch. |
| Quit dirty doc → Discard All | Reloads from disk; in-memory state matches file on relaunch. |
| Ctrl+F | Find bar appears above the tree; TextBox focused. |
| Type `book` | Two `<book>` rows tint yellow; first is blue active. Counter `1 / 2`. |
| Enter / Shift+Enter | Cycles forward/backward; wraps at endpoints. |
| Toggle `Aa` | Case-sensitive search re-runs immediately. |
| Escape | Bar closes; highlights clear; selection preserved. |

## Rough-edges log (v0.9 backlog)

Ordered by friction.

### Resolved 2026-05-09 (Plan 14 + 15)

- [x] ~~**No "Set Schema…" UI for documents without xsi: hints.**~~ Plus opinionated auto-detect that makes the manual gesture rarely needed.
- [x] ~~**Schema cache.**~~ Mtime-keyed.
- [x] ~~**Reload-from-disk on Discard.**~~ Replaces MarkClean lie.
- [x] ~~**No splitter between explorer and node list.**~~ GridSplitter with persisted width.
- [x] ~~**No Find in tree.**~~ Ctrl+F embedded bar with cycle navigation.

### High friction

- [ ] **Replace** — natural Plan 16 successor to Find. CompoundCommand for "Replace All = single undo step".
- [ ] **No way to view multiple documents simultaneously.** Tabs state machinery exists but no renderer.
- [ ] **No XPath query bar / regex search / Find in comments.** Plan 17.
- [ ] **No drag-and-drop reorder of children in the tree.** XML Notepad parity gap.
- [ ] **No cut/copy/paste subtree (Ctrl+X/C/V).** XML Notepad parity gap.
- [ ] **No schema-aware Insert suggestions.** When inserting under `<book>`, suggest `<title>`, `<author>` from the XSD content model — biggest productivity win remaining.
- [ ] **No outline/breadcrumb showing the selected node's full XPath.** XML Notepad parity gap.
- [ ] **No compare-two-documents view.** XML Notepad parity gap.

### Medium friction

- [ ] **Right-click "Apply Schema to..." in the explorer** — unverified WIP commit. Needs a focused Avalonia spike.
- [ ] **LSP refresh and editor LSP-client integration** — blocked on engine release cascade (XQuery 1.3.2 / Xslt 1.3.3).
- [ ] **`MarkClean` on Discard for never-saved docs is still a lie** — only matters if Discard becomes a no-quit flow.
- [ ] **No file-system watcher in the explorer.** External changes don't reflect without re-rooting.
- [ ] **No node-kind icons** (only diagnostic glyphs in the IconKey channel).
- [ ] **No theme switching.** Dark only.
- [ ] **No Tab-key navigation** between name/value cells during edit.
- [ ] **No collapse button for ProblemsView pane.** 120-dip fixed height.
- [ ] **MatchHighlightBrush allocates per binding read** — minor GC pressure on Rebuild. Cache to static brushes if profiling shows it matters.

### Low friction

- [ ] **Editor doesn't accept a folder as a CLI arg yet.** `dotnet run -- /path/to/folder` should land with the explorer rooted there.
- [ ] **GridSplitter drag-to-zero** leaves explorer essentially invisible.
- [ ] **Mutation helpers copy original positions to the replacement node** — misleading after many edits.
- [ ] **Multi-namespace `xsi:schemaLocation`** — only the first ns/location pair is honored.
- [ ] **Recent Files entries with missing files are kept.**
- [ ] **Encoding declaration changes case** on save (`UTF-8` → `utf-8`).
- [ ] **No trailing newline** on saved files.
- [ ] **No app icon.**
- [ ] **No first-launch registration.**
- [ ] **No virtualization beyond `ListBox` default in NodeListView.**

## What's next: Plan 16 candidates

Per the strategic conversation (XML Notepad as gold standard, then expand into XQuery/XSLT editing via existing LSP):

**Track 1 — XML Notepad parity (priority order):**

1. **Replace** (Plan 16) — single, all, dialog flow. Foundation: extend Find with edit gestures + CompoundCommand.
2. **XPath query bar** (Plan 17?) — different mental model than substring Find; filters tree to matching node-set.
3. **Schema-aware Insert suggestions** — biggest productivity win; XSD content-model traversal feeds insert popup.
4. **Cut/Copy/Paste subtree** — Ctrl+X/C/V on tree rows.
5. **Drag-and-drop reorder** — Avalonia DragDrop for child reordering.
6. **Outline/breadcrumb on selection** — selected node's full XPath in status bar + breadcrumb above tree.
7. **Compare two documents** — diff side-by-side.

**Track 2 — LSP integration:** wait for engine release cadence (XQuery 1.3.2 / Xslt 1.3.3 published), then refresh `PhoenixmlDb.LanguageServer` and wire the Avalonia editor as an LSP client for `.xsl` / `.xsd` files. Diagnostics first, then completion / hover / go-to-def. Wraps the existing language server; doesn't reimplement.

**Track 3 — Polish (deferred):** right-click investigation, themes, font customization, status bar enhancements.

## Validated environment

- OS: Linux Mint 22.3 (Cinnamon, X11)
- .NET: 10.0
- Avalonia: 11.2.4
- Material.Icons.Avalonia: 2.x
- Phoenixml.Platform: 0.8.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.4.0 (in-repo)
- PhoenixmlDb.Core: 1.1.0 (NuGet)
- PhoenixmlDb.XQuery: 1.3.1 (NuGet — 1.3.2 in-flight in engine source)
