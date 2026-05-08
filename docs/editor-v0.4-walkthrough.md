# Phoenixml.Editor v0.4 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-07:** v0.4 ships structural editing (Insert / Delete elements and attributes), Compact-on-Save (mark-and-sweep eviction of orphan NodeIds), a Material-icons toolbar, and an editable address bar. Lifts the empty-element restriction in `SetTextContentCommand` now that NodeId allocation exists.

## How to run

```bash
cd /raid/elvogel/repos/phoenixml-platform

# Open empty
dotnet run --project src/Phoenixml.Editor --configuration Release

# Open a file directly via CLI argument
dotnet run --project src/Phoenixml.Editor --configuration Release -- /path/to/file.xml
```

Recommended fixture: `/raid/elvogel/repos/phoenixmldb/devtools/content/tutorials/04-xpath-essentials/input.xml`

## What works in v0.4

### Layout (NEW)

- **Toolbar** above the tree with Open / Save / Undo / Redo icon buttons (Material.Icons.Avalonia). Tooltips on hover; same behavior as the matching keyboard shortcuts and menu items.
- **Address bar** below the toolbar shows the active document's file path. Type a different path + Enter to open it; Escape reverts. Silent skip on bad paths (matches the CLI-arg open semantics).

### Editing (NEW + carried)

- **Right-click any row** for context menu: Insert Child Element / Insert Attribute / Delete.
- **After Insert,** the new row is automatically selected and entered into rename mode with the placeholder name (`newElement` or `newAttr`) selected — type to replace in one gesture.
- **Attribute rename** uses the bare local name (no leading `@` in the TextBox). The `@` prefix is presentation-only on the displayed label.
- **Delete key** on a selected row removes the node. Document-root deletes are silently ignored.
- **F2 inline rename** (carried from v0.2) for elements and attributes.
- **Click-to-edit value** (carried from v0.3) on attribute values and text-only element content. Now works on **empty elements** too — `SetTextContentCommand` allocates a fresh `XdmText` child on the empty path. Clicking an empty value cell is reliable: TextBlock has `Background=Transparent` + `HorizontalAlignment=Stretch` so the full cell area captures the click regardless of value content.
- **Undo/redo** (Ctrl+Z / Ctrl+Y, toolbar buttons, Edit menu). All four edit categories (rename, set value, insert, delete) compose. Insert/Delete don't merge; rename and set-value merge consecutive same-target edits into one undo step.

### File operations (carried)

- File menu Open / Save / Save As / Exit; Ctrl+O Open, Ctrl+S Save.
- CLI file arg.
- **Save runs Compact-on-Save automatically.** Mark-and-sweep eviction of orphan `_byId` entries that aren't reachable from root and aren't held alive by undo/redo command references. Editing sessions don't grow unboundedly.

## Bugs caught and fixed during v0.4

1. **Plan-10 had `TryMergeWith` direction backwards in the SetTextContentCommand sketch.** The implementer caught it during Task 6: `previous` parameter is the new command (not the stack top). Path-gating logic uses `_path == ReplacedExistingText` (this) and `next._path == None` (previous) so only ReplacedExistingText→ReplacedExistingText merges; CreatedNewText edits get separate undo entries.

2. **`UndoCommands` / `RedoCommands` were on the concrete UndoStack, not on `IUndoStack`.** Task 7 needed them on the interface so `Compact()` could walk them through the `Document.Undo` property without a cast. Added to `IUndoStack`.

3. **`EditorAddressBar` template referenced `XmlDocument.DocumentUri`,** which doesn't exist. Actual property is `Source` (`Uri?`) on the `Document` base class. Fixed during Task 11; `Source.IsFile ? Source.LocalPath : Source.ToString()` is cleaner than the `Uri.TryCreate` round-trip the plan sketch had.

4. **Empty value cells weren't clickable.** TextBlock has no Background by default which means no hit-test area when Text is empty. Brand-new attributes (empty value) and empty elements were unclickable. Fix: `Background=Transparent` + `HorizontalAlignment=Stretch` + `Padding="8,0,0,0"` (replacing Margin so the padding is inside the hit-test rect).

5. **No auto-focus / auto-select after Insert.** User had to manually find the new row and press F2. Fix: Insert commands expose `InsertedId` (stable across Revert + redo); NodeListView gains `BeginRenameById` which selects the row, sets `IsEditing`, then dispatches a focus + SelectAll on the realized container's inline TextBox via `Dispatcher.UIThread.Post(...)` (necessary because Avalonia container realization is async). Editor handlers chain: `ExpandById(parent)` → `Execute(cmd)` → `BeginRenameById(cmd.InsertedId)`.

6. **Attribute-name rename inserted `@@`.** F2 on `@id` showed `@id` in the TextBox, user edited to `@newId`, command set `LocalName = "@newId"` verbatim. Fix: added `EditableName` to `ITreeNode`. `XmlTreeNode` returns `LocalName` for both elements and attributes — the `@` prefix on attribute labels is display-only. NodeListView's F2 / BeginRenameById / CommitEdit now use `EditableName` rather than `Label` for the inline TextBox content and the change-detection comparison.

## Architectural validations

- **NodeId allocator + AddNode primitive lift the structural-edit ceiling.** Plan 9's commands all stayed within "swap an existing node under a known id" via `ReplaceNode`. v0.4's Insert commands needed a way to mint fresh ids and add brand-new nodes. The counter-based allocator (`max(parsed-id) + 1` lazy init, increment per call) and the `AddNode` primitive (with a duplicate-id guard) compose cleanly with the existing immutable-replace machinery. Insert = AddNode(new) + ReplaceNode(parent with updated Children). Delete = ReplaceNode(parent without target). Idempotency across Revert + redo is handled by checking whether `_insertedId == default` in Apply.
- **`IEditorCommand.ReferencedNodeIds` keeps Compact-on-Save honest.** Without it, Compact would either evict orphans the undo stack still needs (breaking undo across save) or never evict anything (defeating the point). With it, each command declares the orphans it pins. Most commands (rename, set-value, set-text-replacing) declare nothing — their `_previous` snapshots reference nodes still reachable from root. Insert commands declare `_insertedId` (after first Apply) so the new node survives Revert + redo cycles. DeleteNodeCommand declares the entire deleted subtree. SetTextContentCommand on the CreatedNewText path declares `_newTextId`.
- **Mark-and-sweep is the right shape for orphan cleanup.** Mark phase walks two roots: `_root` BFS through Children/Attributes, and the union of `ReferencedNodeIds` across the undo and redo stacks. Sweep phase removes everything else from `_byId`. Compact runs on Save. Trade-off documented: orphans pinned by long-lived undo entries (e.g., a large-subtree DeleteNodeCommand from many edits ago) stay until the command ages off the stack.
- **`EditableName` is the right ITreeNode addition.** Vocabulary providers can keep presentation prefixes (`@` for attrs, `<!-- -->` for comments) on `Label` while exposing the bare token through `EditableName`. The foundation control stays vocabulary-agnostic; vocabularies pay only one extra method to participate.
- **Toolbar buttons via shared `Action<EditorMainWindow>` handlers.** Same `OpenFileCommand.ExecuteAsync` / `SaveFileCommand.ExecuteAsync` / `UndoCommand.Execute` / `RedoCommand.Execute` that the keyboard shortcuts already used. No code duplication; menu, toolbar, and shortcut all converge on the same handlers.

## Smoke-test results (2026-05-07)

Loaded the catalog fixture. Verified:

| Action | Result |
|---|---|
| Right-click `<product>` → Insert Child Element | New `newElement` row appears as child; selected; F2 mode active; placeholder text selected. Typed `warranty`, Enter → `<warranty/>` lives. |
| Right-click `<product>` → Insert Attribute | New `@newAttr` row appears; F2 active with `newAttr` (no `@`) selected. Typed `sku`, Enter. Clicked into empty value cell, typed `LP-2026`, Enter. Attribute is now `@sku="LP-2026"`. |
| Click empty value cell of new `<warranty/>` | Cell captures click; TextBox appears. Typed `1 year`, Enter. Now `<warranty>1 year</warranty>` (text node created via the lifted empty-element path). |
| Delete key on a row | Row removed, document updates, undo restores. |
| Undo three times via toolbar ↶ | Each edit reverts in reverse order. |
| Redo via toolbar ↷ | Edits replay forward. Inserted nodes reappear with same NodeId (idempotency). |
| Save As `/raid/tmp/v04-edited.xml` | Threading invariant holds; no crash. `xmllint --noout` clean. |
| Address bar Enter on a different valid path | That file opens, address bar updates. |

## Rough-edges log (v0.5 backlog)

Ordered by friction.

### Resolved 2026-05-07 (Plan 10)

- [x] ~~**No way to insert or delete nodes.**~~ Insert/Delete commands shipped.
- [x] ~~**No toolbar.**~~ Material.Icons.Avalonia, Open/Save/Undo/Redo.
- [x] ~~**No address bar.**~~ Editable path field below toolbar.
- [x] ~~**Cannot create text content for empty elements.**~~ `SetTextContentCommand` now allocates an XdmText child on the empty path.
- [x] ~~**Empty value cells unclickable.**~~ TextBlock now captures hits across full cell.

### Resolved 2026-05-07 (Plan 11)

- [x] ~~**No tabs for multiple documents.**~~ Pivoted to a VSCode-style left explorer column instead. Tabs state machinery preserved (clean abstraction, tests pass) for a future renderer. See [`editor-v0.5-walkthrough.md`](./editor-v0.5-walkthrough.md).
- [x] ~~**No recent files menu.**~~ File → Open Recent + `SettingsService` persistence.

### High friction

- [ ] **Insert always appends.** No way to insert at a specific index. Workaround: insert, then manually edit the parent's Children list (not currently exposed). Real fix: "Insert Above" / "Insert Below" context menu items in v0.5.
- [ ] **No tabs for multiple documents.** Single-document only.
- [ ] **Default insertion names are hard-coded.** `newElement`, `newAttr`. A dialog for name + namespace would be more powerful (XmlNotepad does this). Inline F2 covers 80% of the case.

### Medium friction

- [ ] **Auto-F2-then-immediately-type now mostly works** but on cold startup the focus sometimes lands on the previously-active row instead of the new one. Avalonia container realization timing edge case; not always reproducible.
- [ ] **No problems pane / schema validation feedback.** `XmlSchemaValidator` and `ProblemsView` exist; the editor doesn't surface them.
- [ ] **No node-kind icons in the tree.** `XmlTreeNode.IconKey` returns names but no icon assets render them.
- [ ] **No recent files menu.** `SettingsService` is wired but unconsumed by the editor.
- [ ] **Status bar is thin.** Only the document filename. Encoding, line/col, schema-binding, XPath-of-selection are spec'd in §4.7 but unimplemented.
- [ ] **Name column is fixed at 360 dip.** Resizable splitter would help on deeply nested attributes.
- [ ] **No theme switching.** Dark only.
- [ ] **Undo stack has no size limit.** Pinned-orphans-via-DeleteNodeCommand on a large subtree can grow `_byId` significantly. Cap N entries with drop-oldest, with a UX-visible setting.

### Low friction

- [ ] **Encoding declaration changes case** on save (`UTF-8` → `utf-8`). Cosmetic.
- [ ] **No trailing newline on saved files.** POSIX tools complain.
- [ ] **Mixed content rows show text values in quotes.** Styling could be more refined.
- [ ] **XdmText labels truncate at 40 chars.** Long text content gets ellipsized in the label (value column shows it in full).
- [ ] **No app icon.**
- [ ] **No first-launch registration.**
- [ ] **No virtualization beyond Avalonia's ListBox default.** 100k-node docs not validated.
- [ ] **Tab-key navigation between name/value cells during edit.** XmlNotepad does this; we don't.

## What's next: Plan 11 candidates

In priority order:

1. **Tabs for multiple documents.** `EditorState` becomes a list; `EditorMainWindow` builds a tab strip; document picker in the chrome. Foundation work for the rest of the v0.5 features.
2. **Schema validation feedback.** Wire `XmlSchemaValidator` → `ProblemsView`. Click-to-navigate from a problem to the offending tree row.
3. **Recent files** via `SettingsService`. Surface in the File menu.
4. **Insert dialog** for name + namespace on element/attribute insert.
5. **"Insert Above" / "Insert Below"** for positional inserts.
6. **Tab-key navigation** between name and value cells during edit.
7. **Resizable splitter** between name and value columns.
8. **Node-kind icons.** Asset bundle + a bound `Image` in the row template.

## Validated environment

- OS: Linux Mint 22.3 (Cinnamon, X11)
- .NET: 10.0
- Avalonia: 11.2.4
- Material.Icons.Avalonia: 2.x
- Phoenixml.Platform: 0.4.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.2.0 (in-repo)
- PhoenixmlDb.Core: 1.0.30 (NuGet)
