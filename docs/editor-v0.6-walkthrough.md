# Phoenixml.Editor v0.6 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-07:** v0.6 ships schema-validation feedback, a resizable splitter between the explorer and node list, and a save-on-quit prompt for dirty documents. Plus a cosmetic fix that surfaced during the v0.6 smoke test: Material.Icons.Avalonia styles weren't registered, so the toolbar (Plan 10) had been silently invisible across v0.4 / v0.5 / v0.6 builds.

## How to run

```bash
cd /raid/elvogel/repos/phoenixml-platform
dotnet run --project src/Phoenixml.Editor --configuration Release -- /path/to/file.xml
```

For a quick demo of validation, point at an XML doc with an `xsi:noNamespaceSchemaLocation` hint and a sibling XSD it violates. The fixture used during v0.6 smoke-testing:

```
/raid/tmp/v0.6-smoke/
├── library.xsd      # XSD declaring required @isbn + xs:gYear typed <year>
└── library.xml      # Two <book>s, second is missing @isbn and has invalid year
```

## What works in v0.6

### Schema validation (NEW)

- **Background validation** runs after a 500ms debounce on `Document.Changed`. Synchronous on the UI thread (XmlDocument._byId is not thread-safe; the debounce keeps validation off the typing hot path).
- **Schema discovery:** `SchemaResolver` checks `EditorSettings.PerDocumentSchema` first (path → XSD location, persisted), then falls back to `xsi:schemaLocation` / `xsi:noNamespaceSchemaLocation` hints on the document root. No schema → no validation runs.
- **ProblemsView docks at the bottom.** 120-dip pane showing diagnostics with severity glyph, line/col code, and message text. Updates automatically on every validation pass.
- **Best-effort click-to-navigate.** Double-click a diagnostic → editor regex-extracts the first single-quoted token from the message and selects the first `XdmElement` matching that LocalName via breadth-first walk. Misses are silent. Precise diagnostic→node mapping is Plan 13 work — needs source-position tracking on `XdmNode`.

### GridSplitter between explorer and node list (NEW)

- Replaces the v0.5 docked-left explorer + collapse-button block with a `Grid` + `GridSplitter`.
- 4-dip vertical splitter; drag to resize. Sub-pixel changes ignored.
- **Persists position** via `EditorSettings.ExplorerWidth`. First-launch shows the default (250 dip); from second launch the persisted value applies.
- Drop the explicit collapse button — splitter is the resize affordance. Drag close to zero to hide. Plan 13 may add a dedicated button back if user feedback wants it.

### Save-on-quit prompt (NEW)

- Window `Closing` handler walks `EditorState.Tabs` for dirty docs.
- If any are dirty → modal **DirtyDocsConfirmDialog** with three actions:
  - **Save All** — loops through dirty docs, calls `Document.SaveAsync`. Skips never-saved docs (`Source == null`) silently for v0.6.
  - **Discard All** — calls `Document.MarkClean()` on each. Small lie — we're not persisting, just asserting "no unsaved changes" so the second close pass exits cleanly. Safe because the editor exits immediately after.
  - **Cancel** — leaves `args.Cancel = true` in effect; window stays open.
- **Status bar dirty count.** New segment "1 modified" / "N modified" appears whenever any tab has unsaved changes. Surfaces dirty docs that aren't currently visible (no tab strip in v0.6).

### Toolbar icons (FIXED, not new)

- The Plan 10 toolbar (Open / Save / Undo / Redo via Material.Icons.Avalonia) had been silently invisible because `App.axaml` only registered `FluentTheme`. Material.Icons ships its own style dictionary that needs explicit registration:
  ```xml
  <materialIcons:MaterialIconStyles xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia" />
  ```
- Visible from v0.6 onwards.

## Bugs caught and fixed during v0.6

1. **`XmlDocument._byId` isn't thread-safe.** Plan 12's sketch had validation running on `Task.Run`, which would race with user edits. Fixed before code was written: validation runs synchronously on the UI thread; the 500ms debounce keeps it off the typing hot path.
2. **`XdmAttribute.Namespace` is `NamespaceId`, not `string`.** SchemaResolver compares against `NamespaceId.Xsi` (a pre-seeded intern) rather than the URI literal.
3. **Corrupt XSD files throw `XmlException` before `XmlSchemaException`.** SchemaResolver catches both — a malformed XSD doesn't crash validation; it just yields no schema.
4. **`EditorValidationService` is `IDisposable`.** Owns a `CancellationTokenSource` field; CA1001 enforced. Disposal cascades from `EditorMainWindow` (also IDisposable). The Avalonia `App` host is sealed and can't dispose us; suppressed CA2000 in the editor csproj — both objects live for the process lifetime.
5. **Initial column width and async LoadAsync.** `Settings.LoadAsync` is fire-and-forget at construction; the constructor reads `Settings.Settings.ExplorerWidth` before the load completes, so first-launch always shows the default. Documented as a v0.6 trade-off; persisted value applies from second launch.
6. **`Document.MarkClean` was `protected`.** XmlDocument is in a different assembly from the editor. Added `public new void MarkClean()` on XmlDocument forwarding to base — keeps both call sites working without `InternalsVisibleTo`.
7. **Material.Icons.Avalonia silent invisible** (carried over from v0.4). Toolbar icons rendered as empty content because `App.axaml` was missing `<materialIcons:MaterialIconStyles />`. Fixed in this round.

## Architectural validations

- **Synchronous-on-UI-thread validation is fine for v0.6 doc sizes.** Schema validation of the catalog fixture and library fixture completes in low ms. The 500ms debounce dominates user-perceived latency; the validation itself is invisible. Plan 13 may revisit when very large documents arrive — likely fix is a thread-safe snapshot of relevant nodes before handing off to `Task.Run`.
- **Service-driven UI updates work cleanly across all five wired controls.** `EditorState.ActiveDocumentChanged` plus a single subscription per dependent (NodeListView, AddressBar, Explorer, ValidationService, status bar) keeps the wiring readable. Adding ProblemsView didn't require any new event plumbing.
- **`EditorSettings` as a single persisted-state container** generalizes well. Three fields now (`RecentFiles`, `PerDocumentSchema`, `ExplorerWidth`); future fields (theme, last-window-position, opened-tabs-on-restart) can join the same struct without restructuring.
- **GridSplitter persistence pattern.** Drag → DragCompleted → store.UpdateExplorerWidth → fire-and-forget save. No flush-per-pixel; sub-pixel filter avoids spurious saves. Same pattern fits future splitter additions (e.g. ProblemsView height, future right-side panes).

## Smoke-test results (2026-05-07)

Loaded `/raid/tmp/v0.6-smoke/library.xml` (two-book `<library>`, second book missing `@isbn` and with invalid `<year>not-a-year</year>`).

| Action | Result |
|---|---|
| Open file with valid xsi: hint | After ~500ms, ProblemsView populates with the two errors (line 8 missing isbn + line 11 invalid gYear). |
| Toolbar icons | All four (Open / Save / Undo / Redo) render correctly after the MaterialIconStyles fix. |
| Edit invalid year → 1992 | After ~500ms, the gYear error disappears; missing-isbn error persists. |
| Insert Attribute on second book → name `isbn`, value `978-0-321-35668-0` | All errors clear. |
| Drag splitter | Explorer column resizes; persisted on quit + relaunch. |
| Quit with dirty docs | DirtyDocsConfirmDialog appears listing dirty docs by display name; Cancel keeps window open. |
| Status bar dirty count | "1 modified" / "2 modified" updates correctly across edits. |
| Click-to-navigate from a problem | Best-effort selection of the first matching XdmElement; works for the missing-isbn error (jumps to the second `<book>` row); the gYear error message has different shape — no quoted name to extract — selection no-ops. |

## Rough-edges log (v0.7 backlog)

Ordered by friction.

### Resolved 2026-05-07 (Plan 12)

- [x] ~~**No problems pane / schema validation feedback.**~~ Wired end-to-end with click-to-navigate.
- [x] ~~**No splitter between explorer and node list.**~~ GridSplitter with persisted width.
- [x] ~~**No save-on-close prompt.**~~ Save All / Discard All / Cancel modal on window closing.
- [x] ~~**Toolbar icons silently invisible.**~~ MaterialIconStyles registered.

### Resolved 2026-05-09 (Plan 13)

- [x] ~~**No source-position tracking on `XdmNode`.**~~ Core 1.1.0 ships `SourceLine` + `SourceColumn`. See [`editor-v0.7-walkthrough.md`](./editor-v0.7-walkthrough.md).
- [x] ~~**Click-to-navigate is regex-based.**~~ Push-based validator + `DiagnosticLocation` now attach the exact offending NodeId. Click-to-navigate selects precisely; inline ✕ / ⚠ glyphs land on the right rows.
- [x] ~~**No inline error overlays.**~~ Glyphs in front of offending rows.

### High friction (was)

- [ ] **No source-position tracking on `XdmNode`.** Click-to-navigate is regex+name-only; works on simple "element 'X' is missing" messages but degrades on complex diagnostics. Plan 13 should add line/col fields to XdmNode (Core-side change with downstream impact on XQuery/XSLT runtime).
- [ ] **No way to view multiple documents simultaneously.** Tabs state exists but no renderer.
- [ ] **No "Set Schema…" UI** for documents without xsi: hints. User must edit `EditorSettings.PerDocumentSchema` JSON directly to override.
- [ ] **No inline error overlays** on offending tree rows (red squiggle / icon).

### Medium friction

- [ ] **Per-document Save/Discard/Cancel choice in the quit dialog.** v0.6 is bulk-only.
- [ ] **`MarkClean` on Discard All is a lie** — fine because the editor exits immediately, but if Plan 13 adds a "close without quitting" path the doc would be marked clean while in-memory state is still divergent from disk. Reload-from-disk on Discard is the proper fix.
- [ ] **Save All silently skips never-saved docs.** Should force Save As for them.
- [ ] **Schema cache.** `LoadSchema` re-reads the XSD on every validation pass. Cache by path + timestamp.
- [ ] **First-launch ExplorerWidth shows default.** Async settings load completes after the column is constructed.
- [ ] **No FileSystemWatcher in the explorer.** External changes don't reflect.
- [ ] **No node-kind icons** in the tree or explorer.
- [ ] **No theme switching.** Dark only.
- [ ] **No Tab key navigation** between name/value cells during edit.
- [ ] **No collapse button for ProblemsView pane.** 120-dip fixed height. Drag affordance for height? Future.

### Low friction

- [ ] **xsi:schemaLocation with multiple ns/location pairs** — v0.6 takes only the first pair. Multi-namespace docs need handling.
- [ ] **GridSplitter drag-to-zero** leaves explorer essentially invisible without a way to bring it back. Could add a min-width or a context menu "show explorer" option.
- [ ] **Recent Files entries with missing files are kept.** Click does nothing if `File.Exists` returns false.
- [ ] **Encoding declaration changes case** on save (`UTF-8` → `utf-8`).
- [ ] **No trailing newline** on saved files.
- [ ] **Mixed-content text rows show values in quotes.** Styling could be more refined.
- [ ] **No app icon.**
- [ ] **No first-launch registration.**
- [ ] **No virtualization beyond `ListBox` default in NodeListView.**

## What's next: Plan 13 candidates

In priority order:

1. **Source-position tracking on XdmNode** — line/col fields populated during parsing, surfaced via diagnostic→node mapping. Touches `PhoenixmlDb.Core`'s parser; needs careful release coordination with XQuery/XSLT runtime.
2. **Inline error overlays** on tree rows once diagnostic→node mapping is precise.
3. **"Set Schema…" UI** — File menu entry + folder picker + persistence into `EditorSettings.PerDocumentSchema`.
4. **Per-document Save/Discard/Cancel** in the quit dialog.
5. **Reload-from-disk** when Discard is selected without quitting.
6. **Schema cache** to avoid re-parsing on every validation.
7. **FileSystemWatcher** in the explorer.
8. **Tab strip retry** with custom layout panel.
9. **ProblemsView collapse + height splitter.**

## Validated environment

- OS: Linux Mint 22.3 (Cinnamon, X11)
- .NET: 10.0
- Avalonia: 11.2.4
- Material.Icons.Avalonia: 2.x
- Phoenixml.Platform: 0.6.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.2.0 (in-repo)
- PhoenixmlDb.Core: 1.0.30 (NuGet)
