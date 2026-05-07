# Phoenixml.Editor v0.3 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-07:** v0.3 adds value-column editing on top of v0.2's name-column rename. Click any value cell, type, press Enter — the change flows through `SetAttributeValueCommand` (attributes) or `SetTextContentCommand` (text-only elements) on the document's `IUndoStack`. Ctrl+Z / Ctrl+Y undo/redo. Save round-trips through the XDM serializer.

This doc captures what works, the bugs caught during smoke-testing, and the v0.4 backlog.

## How to run

```bash
cd /raid/elvogel/repos/phoenixml-platform

# Open empty
dotnet run --project src/Phoenixml.Editor --configuration Release

# Open a file directly via CLI argument
dotnet run --project src/Phoenixml.Editor --configuration Release -- /path/to/file.xml
```

Recommended fixture for the demo:
`/raid/elvogel/repos/phoenixmldb/devtools/content/tutorials/04-xpath-essentials/input.xml`

## What works in v0.3

### Editing — name column (carried from v0.2)

- **F2 inline rename** in the name column for elements and attributes.
- Routes through `RenameElementCommand` / `RenameAttributeCommand`.

### Editing — value column (NEW in v0.3)

- **Single-click on a value cell** swaps the TextBlock for a TextBox. Enter commits, Escape cancels, click-away auto-commits.
- **Attributes:** routes to `SetAttributeValueCommand`. Empty-string values are valid.
- **Text-only elements** (single XdmText child, no element children, no mixed content): routes to `SetTextContentCommand`. Replaces the existing text node's `Value` — element id and Children list stay valid, no NodeId allocation needed.
- **Container / mixed / empty elements:** silently no-op. The row rebuilds with its prior (empty) value. Insert-text-into-empty-element is Plan 10 work.
- **Special characters** are correctly entity-encoded on save (`&` → `&amp;`, `<` → `&lt;`, `"` in attribute values → `&quot;`).
- **Undo/redo** fully wired. Ctrl+Z / Ctrl+Y collapse consecutive edits to the same node into a single undo step (`TryMergeWith` semantics).

### File operations (carried from v0.2)

- File menu Open / Save / Save As / Exit; Ctrl+O Open, Ctrl+S Save.
- CLI file argument: `dotnet run -- /path/to/file.xml`.
- Save / Save As round-trip via `XmlDocument.SaveAsync` (XDM serializer); produces clean indented UTF-8 XML thanks to `PhoenixmlDb.Core 1.0.30`.

## Bugs caught and fixed during v0.3

1. **Save As crashed with "Call from invalid thread"** (pre-existing v0.2 bug, surfaced when smoke-testing v0.3). `XmlDocument.SaveAsAsync` does file I/O which resumes off the UI thread; `MarkClean` on the worker thread fires `Document.Changed` → `XmlTreeNodeProvider.Changed` → `NodeListView.OnProviderChanged` → `Rebuild` → `FindControl` (UI-thread-only) → throw. Fix: marshal `OnProviderChanged` via `Dispatcher.UIThread.Post` when not already on the UI thread. The same pattern should now be applied wherever a foundation control subscribes to `Document.Changed`.

2. **Plan-9 had `TryMergeWith` direction backwards in the draft.** Implementer caught it during Tasks 1-2 by reading the v0.2 `RenameAttributeCommand` first. `this` is the already-applied command (holds the original snapshot in `_previous`); the parameter is the new command. Merged result keeps `prev._previous` so undo restores the pre-FIRST-edit value, not the most recent intermediate one. Same merge pattern applies for both new commands.

3. **`SetTextContentCommand` cannot create a text child for empty elements.** `XmlDocument.ReplaceNode` swaps an existing entry under a known NodeId; creating a new XdmText needs NodeId allocation infrastructure (where do new ids come from?). For v0.3 the command throws `InvalidOperationException` on empty elements; the editor silently ignores the value-change request for that case so the user just sees the row redraw with its prior value. Plan 10 will introduce a NodeId allocator + `Children`-list mutation primitive and lift this restriction.

4. **Tests use PascalCase, not snake_case.** The codebase enables CA1707 with `TreatWarningsAsErrors=true`. Plan 9 specified test names like `Apply_SetsAttributeValue`; implementer renamed all to PascalCase (`ApplySetsAttributeValue`) to match the v0.2 `RenameAttributeCommandTests` convention. Worth remembering for future plans.

5. **`Assert.Contains` requires explicit `StringComparison`.** CA1307 enforced. Add `StringComparison.Ordinal` for the throw-message assertions in `SetTextContentCommandTests`.

## Architectural validations

- **Click-vs-F2 split for the two columns works.** F2 from anywhere edits the name; single-click in the value cell edits the value. Two independent gestures, both discoverable. Tab navigation between cells during edit (XmlNotepad-style) is a v0.4 polish item.
- **The `IUndoStack` foundation scales to the second category of edit.** Plan 8 added two rename commands; Plan 9 added two value commands. Both use the same immutable-replace pattern (build new XDM node with changed property, swap via `XmlDocument.ReplaceNode`). The `TryMergeWith` semantics generalize cleanly. Plan 10's Insert/Delete will be the first commands that *don't* fit this pattern — they need NodeId allocation and parent `Children`-list mutation.
- **`Document.Changed` event-driven view refresh holds.** All four edit commands trigger `Document.Changed`; the provider re-raises; NodeListView rebuilds. No imperative "refresh this row" wiring. The threading marshal in `OnProviderChanged` is the only invariant the foundation control adds.
- **Two-stage edit path keeps the foundation control vocabulary-agnostic.** NodeListView raises `NodeRenameRequested` and `NodeValueChangeRequested`; the editor app decides which command (or none) to issue. Workbench's XSLT/XQuery editors can supply different commands to the same control with no foundation changes.

## Smoke-test results (2026-05-07)

Loaded `/raid/elvogel/repos/phoenixmldb/devtools/content/tutorials/04-xpath-essentials/input.xml` (63-line `<catalog>`), edited:

| Change | Before | After |
|---|---|---|
| Attribute (`@name`) on `<category>` | `name="Electronics"` | `name="Electronics & Gadgets"` |
| Text-only element (`<name>`) under product | `Laptop Pro` | `Laptop Pro V` |

Saved to `/raid/tmp/value-edited.xml`. `xmllint --noout` reports well-formed. Diff against original shows exactly the intended changes plus the two cosmetic items below — no other lines touched.

## Rough-edges log (v0.4 backlog)

Ordered by friction.

### Resolved 2026-05-07 (Plan 9)

- [x] ~~**Value column is read-only.**~~ Click-to-edit on attributes and text-only elements; commits via `SetAttributeValueCommand` / `SetTextContentCommand` on the document's `IUndoStack`.

### High friction

- [ ] **No way to insert or delete nodes.** Need `InsertElementCommand`, `InsertAttributeCommand`, `DeleteNodeCommand`. Blocked on a NodeId allocator (where do new ids come from?) and `Children`-list mutation infrastructure. Plan 10's first design decision.
- [ ] **Cannot create text content for empty elements.** Same NodeId-allocation blocker. `<empty/>` shows an empty value cell; clicking + typing fires `NodeValueChangeRequested` but the editor's routing recognizes the empty-element case and silently no-ops. Sub-task of Plan 10.
- [ ] **No address bar / file path indicator.** XmlNotepad has an editable address bar at the top.
- [ ] **F2-then-immediately-type doesn't auto-focus the TextBox.** Avalonia container realization timing. Polish item.

### Medium friction

- [ ] **No Tab-key navigation between cells during edit.** XmlNotepad uses Tab to move between name and value cells. Would need NodeListView to keep edit mode active across the column boundary.
- [ ] **No toolbar with Open/Save/Undo/Redo icons.** Menu + keyboard cover the same ground but a toolbar matches XmlNotepad UX.
- [ ] **No tabs for multiple documents.** `EditorState.ActiveDocument` is a single nullable field; opening a second file replaces the first.
- [ ] **No recent files menu.** `SettingsService` is wired but unconsumed by the editor.
- [ ] **No problems pane / schema validation feedback.** `XmlSchemaValidator` and `ProblemsView` exist; the editor doesn't surface them.
- [ ] **No node-kind icons.** `XmlTreeNode.IconKey` returns names but no icon assets render them.
- [ ] **Status bar is thin.** Only the document filename. Encoding, line/col, schema-binding, XPath-of-selection are spec'd in §4.7 but unimplemented.
- [ ] **Name column is fixed at 360 dip.** Resizable splitter or dynamic sizing would help on deeply nested attributes.
- [ ] **No theme switching.** Dark only.

### Low friction

- [ ] **Save changes capitalization** of the encoding declaration (`UTF-8` → `utf-8`). Cosmetic; comes from `Utf8StringWriter` reporting lowercase. If we want to preserve the original casing we'd need to record it on Open and replay it on Save.
- [ ] **No trailing newline on saved files.** `xmllint`-clean but POSIX tools complain. Trivial to add to the serializer.
- [ ] **No visual hint that text-only leaves with attributes still expand.** A subtle "+" or "..." hint could help on rows like `<price currency="USD">1299.99</price>`.
- [ ] **Mixed content (text interleaved with elements) shows text rows with quoted values.** Styling could be more refined (italic? different color?).
- [ ] **XdmText labels still truncate at 40 chars with `…`.** Long text content gets cut off in the label even though the value column shows it in full.
- [ ] **No app icon.** Default Avalonia icon in the taskbar.
- [ ] **No first-launch registration** — gated on phoenixml.net licensing backend.
- [ ] **No virtualization beyond what `ListBox` does for free.** 100k-node documents not yet validated.

## What's next: Plan 10 candidates

In priority order:

1. **Insert / Delete commands.** Need NodeId allocator (likely a counter on `XmlDocument` paired with persistence in the document model). Once allocator exists, lift the empty-element restriction in `SetTextContentCommand`.
2. **Toolbar with Open/Save/Undo/Redo icons** — matches XmlNotepad.
3. **Address bar** at the top showing/editing the current file path.
4. **Tabs for multiple documents** — `EditorState` becomes a list, document picker in the chrome.
5. **Recent files** via `SettingsService`.
6. **Schema validation feedback** — wire `XmlSchemaValidator` → `ProblemsView`, click-to-navigate.
7. **Tab-key navigation between name and value cells** during edit.

## Validated environment

- OS: Linux Mint 22.3 (Cinnamon, X11)
- .NET: 10.0
- Avalonia: 11.2.4
- Phoenixml.Platform: 0.3.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.1.0 (in-repo)
- PhoenixmlDb.Core: 1.0.30 (NuGet)
