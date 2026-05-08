# Phoenixml.Editor v0.5 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-07:** v0.5 ships a VSCode-style left explorer column for folder-scoped XML editing, plus persisted Recent Files via `SettingsService`. The original Plan 11 included a tab strip for multi-document editing — Avalonia's `TabStrip` and `ItemsControl` rendered unreliably in our DockPanel layout despite the state machinery being correct (verified via diagnostic logging: tab count went 0→1→2 but only the active tab rendered). User pivot: drop the tab strip; ship the explorer instead. The tabs *state* is preserved (clean abstraction, tests still green) so a future renderer can plug in without rework.

## How to run

```bash
cd /raid/elvogel/repos/phoenixml-platform

# Open empty (use Open Folder… from the explorer)
dotnet run --project src/Phoenixml.Editor --configuration Release

# Open a file directly via CLI argument; explorer auto-roots to its folder
dotnet run --project src/Phoenixml.Editor --configuration Release -- /path/to/file.xml
```

## What works in v0.5

### Left explorer column (NEW)

- **Folder tree** of XML files (`*.xml`, `*.xsd`, `*.xsl`, `*.xslt`, `*.sch`).
- Subfolders are filtered: only those containing XML content (recursive) appear, reducing noise.
- **Lazy expansion** — child folders enumerate on first expand, not at load time.
- **Double-click a file** → routes through `OpenFileOnLaunch` (silent skip on bad paths, same as the address bar).
- **Open Folder…** button at the top — explicit folder picker, sets the root regardless of active document.
- **Auto-root** to the active document's parent on the first document open. Subsequent opens that fall *under* the current root preserve it (no re-anchoring on every double-click); opens outside the tree still follow.
- **Collapse toggle** on the column's right edge (`◀`/`▶`) hides the explorer while keeping the toggle button visible — restores the working area on narrow windows.

### Address bar (carried + extended)

- File paths still load as before.
- **NEW:** directory paths re-root the explorer instead of being silently ignored.

### Recent Files (NEW)

- `SettingsService`-backed JSON at `~/.config/phoenixml/Phoenixml.Editor/settings.json` (Linux; platform-canonical paths on macOS / Windows).
- **File → Open Recent** submenu, most-recent-first, capped at 10.
- Records on every successful Open and Save As.
- Dedupes by path (re-opening a file moves it to the top).
- Survives editor restart.

### Tabs state machinery (preserved, unrendered)

- `EditorState.Tabs`, `OpenDocument(XmlDocument)`, `CloseTab(DocumentTab)` all work as designed.
- Backwards-compat `ActiveDocument` setter routes to `OpenDocument`.
- 8 unit tests (`EditorStateTabsTests`) green.
- No UI surface — the tab strip was removed pending a renderer that lays out reliably in our DockPanel.

## Bugs caught and fixed during v0.5

1. **`UndoCommands` / `RedoCommands` were on the concrete `UndoStack` only.** Earlier (Plan 10) work needed them on `IUndoStack` for Compact-on-Save. Fixed in Plan 10; the same property names made tab-collection iteration trivial in Plan 11.
2. **`EditorSettings` couldn't be `public` due to CA1002** (`List<T>` on a public member). Made internal — nothing external references it. No behavioral change.
3. **`SettingsService.LoadAsync` returns null on missing file.** Treated as "start fresh." Tests cover the missing/corrupt cases.
4. **xUnit2013 + `TreatWarningsAsErrors`** required swapping `Assert.Equal(1, col.Count)` → `Assert.Single(col)` and `Assert.Equal(0, col.Count)` → `Assert.Empty(col)` across the new test project.
5. **Avalonia `TabStrip` rendered only the active tab in our DockPanel layout.** Diagnostic logging confirmed the state was correct (tab count grew on each open). Switched to `ItemsControl` + horizontal `StackPanel` — same behavior. Pivoted to the explorer instead. Tabs state preserved for future use.
6. **Plan-11 sketch referenced `XmlDocument.DocumentUri`,** which doesn't exist. Actual property is `Source : Uri?` on the `Document` base class. Address bar updated; cleaner than the planned `Uri.TryCreate` round-trip.
7. **Avalonia's `TreeView` toggles expansion on row double-click** by default. Our handler returned without consuming the event for folder rows, so the default toggle ran and collapsed the just-expanded folder. Fix: set `e.Handled = true` for both folder and file double-clicks; folders skip the open path; chevron remains the (correct) expand gesture.
8. **Explorer auto-rooted on every active-document change,** so double-clicking a deep file collapsed the project view to that file's folder. Fix: only re-root when the newly-opened file's folder is NOT under the current root. Same-tree opens preserve the user's chosen root.

## Architectural validations

- **Explorer is not vocabulary-aware.** Unlike `NodeListView` (which uses `ITreeNodeProvider` to stay XML-agnostic), the explorer talks directly to the file system. That's fine — it's an editor-app-level concern, not a foundation-control concern. If a Workbench app wants a different left column, it builds its own.
- **`ExplorerNode` lazy expansion via placeholder.** Avalonia's `TreeView` shows expand chevrons based on `Children` non-emptiness. Adding a single placeholder child on every directory makes folders expandable without enumerating their contents. The `Expanded` event handler then calls `EnsureChildrenLoaded` to populate the real children. Idempotent — repeated calls are no-ops after the first.
- **Persistence pattern: model + store + change event.** `EditorSettings` is the JSON shape; `EditorSettingsStore` wraps `SettingsService` with a debounced background save and a `Changed` event. Menus that depend on settings (`RecentFilesMenu`) subscribe to `Changed` and rebuild on each fire. Will reuse this pattern when adding theme persistence, last-window-position, etc.
- **Tabs state survives the renderer pivot.** Keeping `OpenDocument`/`CloseTab` semantics correct (with tests) means the abstraction is portable — a future renderer (custom horizontal `StackPanel` with proper measure/arrange overrides, or even VSCode-style stacked editors) plugs in without changing state.
- **EditorMainWindow surface for cross-control wiring.** Adding `internal Explorer { get; }` lets the address bar retarget the explorer's root without injecting both controls into a separate coordinator. Each new chrome element ends up either a sibling of `_state` (state-driven) or accessed via a window property (view-driven). Clear pattern.

## Smoke-test results (2026-05-07)

| Action | Result |
|---|---|
| Open file via menu / Ctrl+O | Loads, explorer auto-roots to its folder. |
| Address bar Enter on a folder path | Explorer re-roots; no document opens. |
| Address bar Enter on a file path | Document loads, explorer stays put if file is under current root. |
| Explorer double-click on file | Document loads, tree stays anchored at chosen root. |
| Explorer double-click on folder | Expands and stays expanded. |
| File → Open Recent → click | Opens; if folder is outside current root, explorer follows. |
| Editor restart | Recent Files list persists. |
| Collapse toggle | Tree hides, button stays visible, click again to restore. |

## Rough-edges log (v0.6 backlog)

Ordered by friction.

### Resolved 2026-05-07 (Plan 11)

- [x] ~~**No tabs for multiple documents.**~~ Pivoted to a left explorer column. Tabs state preserved for future renderer.
- [x] ~~**No recent files menu.**~~ File → Open Recent + persisted via `SettingsService`.
- [x] ~~**No address bar / file path indicator.**~~ Already shipped in Plan 10; v0.5 extends it to accept folder paths too.

### High friction

- [ ] **No way to view multiple documents simultaneously.** Tabs state exists but no renderer. Either fix the TabStrip layout issue (likely a measure/arrange interaction with our outer DockPanel + LastChildFill setup), build a custom `StackPanel`-with-explicit-widths tab strip, or pivot to a split-editor layout (VSCode-style).
- [ ] **Save-on-close prompt for dirty documents.** Currently silent overwrite on Open replacing the active document. With explorer-based navigation this is less surprising than tabs would be, but still risky.
- [ ] **No problems pane / schema validation feedback.** `XmlSchemaValidator` and `ProblemsView` exist; the editor doesn't surface them. Plan 12 candidate.
- [ ] **Insert always appends.** No "Insert Above" / "Insert Below."

### Medium friction

- [ ] **Explorer doesn't refresh on external file changes.** If you save a file outside the editor, the explorer doesn't pick up new files. Need a `FileSystemWatcher` per root.
- [ ] **No node-kind icons in the tree (NodeListView) or in the explorer.** Plain `▸` / `•` glyphs as a stopgap.
- [ ] **No name+namespace dialog on Insert.** XmlNotepad has one; we use placeholder names + F2.
- [ ] **Status bar is thin.** Only document name. Encoding, line/col, schema-binding spec'd but unimplemented.
- [ ] **No theme switching.** Dark only.
- [ ] **No tab key navigation between name and value cells during edit.**
- [ ] **No splitter between explorer and node-list columns.** Width is fixed at 250 dip; no resize affordance.

### Low friction

- [ ] **Explorer file-content filter is hardcoded.** A settings-driven extension list would let users surface other vocabularies (e.g., `.config`, `.props`).
- [ ] **Recent Files entries with missing files are kept in the list.** Click does nothing if `File.Exists` returns false. Could grey them out or auto-prune after N misses.
- [ ] **Encoding declaration changes case** on save (`UTF-8` → `utf-8`).
- [ ] **No trailing newline on saved files.**
- [ ] **Mixed-content text rows show values in quotes.** Styling could be more refined.
- [ ] **No app icon.**
- [ ] **No first-launch registration.**
- [ ] **No virtualization beyond `ListBox` default in NodeListView.**

## What's next: Plan 12 candidates

In priority order:

1. **Schema validation feedback.** Wire `XmlSchemaValidator` → `ProblemsView`. Click-to-navigate from a problem to the offending tree row.
2. **Save-on-close prompt** for dirty documents (works regardless of single-doc or future tabs UI).
3. **Splitter between explorer and node-list.** GridSplitter with persisted width via `EditorSettings`.
4. **Tab strip retry.** Custom horizontal layout panel with explicit per-item widths; fallback if the foundation TabStrip continues to misbehave.
5. **FileSystemWatcher in the explorer.** Live updates on external changes.
6. **Node-kind icons.** Asset bundle + bound `Image` in templates.
7. **Insert dialog** for name + namespace.
8. **Tab-key navigation** between name and value cells during edit.

## Validated environment

- OS: Linux Mint 22.3 (Cinnamon, X11)
- .NET: 10.0
- Avalonia: 11.2.4
- Material.Icons.Avalonia: 2.x
- Phoenixml.Platform: 0.5.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.2.0 (in-repo)
- PhoenixmlDb.Core: 1.0.30 (NuGet)
