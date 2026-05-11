# Phoenixml.Editor v0.9 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-10:** v0.9 ships **Replace** (Plan 16) — completing the Find/Replace gesture started in Plan 15. Substring replacement honoring case-sensitivity, scoped across element/attribute names, attribute values, and text content. Replace All runs as a single `CompoundCommand` for one-click undo.

## How to run

```bash
cd /raid/elvogel/repos/phoenixml-platform
dotnet run --project src/Phoenixml.Editor --configuration Release -- \
  /raid/tmp/v0.8-smoke/data-and-schemas-by-type/data/library.xml
```

## What works in v0.9

### Replace (Plan 16) — NEW

- **Ctrl+H** opens the find bar in Replace mode (Replace row expanded, Replace TextBox focused).
- **Ctrl+F** continues to open Find-only mode (Replace row collapsed or stays as user left it).
- **Chevron toggle** (▸/▾) on the bar collapses/expands the Replace row.
- **Replace** button — replaces the current active match; advances find afterwards (via Document.Changed → debounced re-run).
- **Replace All** button — single `CompoundCommand` wraps all per-match commands; one Ctrl+Z reverts the whole batch.
- **Buttons-only triggers** — no Enter binding on the Replace TextBox; deliberately avoids destructive mis-fires.
- **Empty replacement allowed** — cleanup gesture (find `xsi:`, replace empty).
- **Document.Changed subscription** when the bar is open — matches re-compute automatically after each replace. Active index clamps to the new (shrunk) match list.

### Carried from v0.8

- Schema management cascade (auto-detect, smart-root, cache, picker, reload-on-discard)
- Find in tree (Ctrl+F, three-state row tint, cycle navigation)
- Inline validation glyphs, click-to-navigate, splitter, save-on-quit

### Architecture pieces added in v0.9

- **`CompoundCommand`** in `Phoenixml.Platform.Editor.Xml.Editing` — wraps an ordered list of `IEditorCommand`. Apply runs forward; Revert reverses. Single undo step. Reusable foundation for any future "do many things atomically" gesture (drag-reorder, schema-aware refactors, etc.).
- **`ReplaceService`** in `Phoenixml.Platform.Editor.Xml.Find` — given a match list and `(query, replacement, matchCase)`, emits vocabulary commands per matching node kind: `RenameElementCommand` for elements, `RenameAttributeCommand` + `SetAttributeValueCommand` for attributes (both fire when both match), `SetTextContentCommand` for text nodes (resolved to the parent element).
- **`FindController.OnReplaceCurrent` / `OnReplaceAll`** — wires the bar's replace events through `ReplaceService` to the document's `IUndoStack`.

## Bugs caught and fixed during v0.9

1. **`ReplaceService.BuildReplacements` static promotion.** Originally an instance method; CA1822 caught that it had no instance state. Promoted to `static` — slightly cleaner API surface, no allocator overhead for callers.
2. **Test fixture noise on `BuildReplacementsForAttributeName`.** The original library fixture's attribute values (e.g. `"book-isbn-001"`) also contain `isbn`, which made the test produce both rename + set-value commands when it expected only rename. Fixed by using a dedicated fixture where the query substring appears in the local name only.
3. **`XdmText.Parent` null-safety.** Defensive null-check in the text-node branch of `ReplaceService` (orphaned text in theory shouldn't appear; in practice the check costs nothing).

## Architectural validations

- **`CompoundCommand` is the missing piece for atomic gestures.** Plan 8's `IEditorCommand` was per-mutation; everything since has been one-at-a-time. Replace All demanded a batch primitive, and `CompoundCommand` answers it minimally — child commands run in order, revert in reverse. Same shape will fit drag-reorder (delete + insert), future schema-aware refactors ("rename element + update all xsi:type attributes that reference it"), and any future macro-style gestures.
- **Refresh-on-Document.Changed is the right edit-loop primitive.** The find bar subscribes when open; the controller's `RefreshMatches()` re-runs against the modified tree; the bar's counter updates automatically. The pattern generalizes: any UI surface that depends on "live document state" can subscribe to `Document.Changed` and the change propagates without coordination.
- **`ReplaceService` stays vocabulary-aware via the existing command types.** No new mutation primitives introduced for v0.9 — the service is pure orchestration over Plan 8/9 commands. That's exactly the seam we want.

## Smoke-test results (2026-05-10)

Loaded `library.xml` and exercised the full Replace flow:

| Action | Result |
|---|---|
| Ctrl+H | Find bar opens with Replace row expanded; Replace TextBox focused. |
| Find `book`, Replace `tome`, click Replace | First `<book>` becomes `<tome>`. Match list re-runs after Document.Changed; counter goes from 2 to 1. |
| Click Replace All | Remaining `<book>` becomes `<tome>`. Counter `0 / 0`. |
| Ctrl+Z | All replacements revert in one undo. Find re-runs; counter back to `2 / 2`. |
| Find `Brian`, Replace `Bryan`, Replace | Text content updates correctly. |
| Chevron toggle | Replace row collapses/expands. |
| Ctrl+F when bar already open | Focuses Find TextBox; Replace row state preserved. |
| Escape | Bar closes; highlights clear; selection preserved. |
| Find `xsi:`, Replace empty | Attribute rename to empty string throws silently (per design); doc state may diverge — flagged as Plan 17 backlog item (failed-replace tally). |

## Rough-edges log (v1.0 backlog)

### Resolved 2026-05-10 (Plan 16)

- [x] ~~**No Replace**~~ — Ctrl+H, embedded Replace row, single + all, single undo for all via CompoundCommand.

### High friction

- [ ] **Failed-replace reporting** — when Replace All produces commands that throw (e.g. invalid XML names after substring replacement), they fail silently. Need a counter ("3 of 17 replacements skipped — invalid names") or per-replace error surfacing.
- [ ] **No XPath query bar** — different mental model than substring Find (filter tree to matching node-set vs. iterate matches). Big productivity win for power users.
- [ ] **No regex search/replace.**
- [ ] **No schema-aware Insert suggestions** — biggest productivity win remaining in the XML Notepad parity list.
- [ ] **No cut/copy/paste subtree (Ctrl+X/C/V).**
- [ ] **No drag-and-drop reorder of children.**
- [ ] **No outline/breadcrumb showing the selected node's full XPath.**
- [ ] **No compare-two-documents view.**

### Medium friction

- [ ] **Right-click "Apply Schema to..." in the explorer** — still WIP from v0.8.
- [ ] **LSP refresh / editor LSP-client integration** — still blocked on engine release cadence.
- [ ] **No file-system watcher in the explorer.**
- [ ] **No node-kind icons.**
- [ ] **No theme switching.**
- [ ] **No Tab-key navigation between name/value cells during edit.**

### Low friction

- [ ] **Editor doesn't accept a folder as a CLI arg** (`dotnet run -- /some/folder` should root the explorer).
- [ ] **Brush allocation on row binding reads** — `MatchHighlightBrush` / `DiagnosticGlyphBrush` allocate per access. Cache statically.
- [ ] **Multi-namespace `xsi:schemaLocation`** — only the first ns/location pair is honored.
- [ ] **GridSplitter drag-to-zero** leaves explorer essentially invisible.

## What's next: Plan 17 candidates

Per the XML Notepad parity backlog, in priority order:

1. **Schema-aware Insert suggestions** — XSD content-model traversal feeds the insert popup. Biggest productivity win remaining.
2. **XPath query bar** — filter tree to matching node-set; click to navigate.
3. **Cut / Copy / Paste subtree** — clipboard API + existing CompoundCommand for the multi-step paste.
4. **Outline / breadcrumb on selection** — selected node's full XPath in status bar + breadcrumb above tree.
5. **Failed-replace reporting** — small polish on Plan 16's Replace All.
6. **Drag-and-drop reorder.**
7. **Compare two documents.**

LSP integration still waits on engine release cadence (XQuery 1.3.2 / Xslt 1.3.3 publishing).

## Validated environment

- OS: Linux Mint 22.3 (Cinnamon, X11)
- .NET: 10.0
- Avalonia: 11.2.4
- Material.Icons.Avalonia: 2.x
- Phoenixml.Platform: 0.9.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.5.0 (in-repo)
- PhoenixmlDb.Core: 1.1.0 (NuGet)
- PhoenixmlDb.XQuery: 1.3.1 (NuGet)
