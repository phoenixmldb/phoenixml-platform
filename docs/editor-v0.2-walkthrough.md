# Phoenixml.Editor v0.2 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-07:** Editor evolved from "tree + serialized XML text" (v0.1) to XmlNotepad-style dual-pane (tree of names + aligned column of values), with F2 inline rename for elements and attributes, full Undo/Redo via `IUndoStack`, and CLI file-argument support.

This doc captures what works, what's intentionally minimal, and what's next.

## How to run

```bash
cd /raid/elvogel/repos/phoenixml-platform

# Open empty (use File → Open after)
dotnet run --project src/Phoenixml.Editor --configuration Release

# Open a file directly via CLI argument
dotnet run --project src/Phoenixml.Editor --configuration Release -- /path/to/file.xml
```

Recommended fixture for the demo:
`/raid/elvogel/repos/phoenixmldb/devtools/content/tutorials/04-xpath-essentials/input.xml`

## What works in v0.2

### Layout

- **One unified `NodeListView` control**: a flat virtualized list, two columns per row.
- **Name column** (360 dip): depth-indent + chevron (`▶`/`▼`) + node label.
- **Value column** (`*`): the node's value (attribute value, text-content of leaf elements, comment text, PI value).
- **1-dip vertical divider** between columns (themed with `Phoenixml.Color.Border`).
- Container rows (elements with element children) leave the value column blank.
- First three levels auto-expand on Open so values are visible immediately on launch.

### Editing

- **F2 inline rename** in the name column for both elements and attributes.
  - Selected row's name TextBlock swaps to a TextBox.
  - Enter commits, Escape cancels, click-away auto-commits.
  - Routes through `RenameElementCommand` (XdmElement) or `RenameAttributeCommand` (XdmAttribute) on the document's `IUndoStack`.
- **Ctrl+Z / Ctrl+Y** undo / redo. View refreshes automatically via `Document.Changed`.
- **Edit menu** mirrors the keybindings.

### File operations

- **File menu** with Open, Save, Save As, Exit.
- **Ctrl+O** Open, **Ctrl+S** Save (window-wide keybindings).
- **CLI file arg**: `dotnet run -- /path/to/file.xml` opens that file on launch. Silent skip if the path is invalid — no scary startup dialog.
- Save / Save As round-trip through `XmlDocument.SaveAsync` (XDM serializer); produces clean indented UTF-8 XML thanks to the `PhoenixmlDb.Core 1.0.30` serializer fix from Plan 7.

### Other foundation niceties (carried over from v0.1)

- Custom window chrome (no stacked OS title bar).
- Status bar shows the active document filename.
- Title bar updates with the open file's name.
- Phoenixml dark theme applied at startup; tree/value columns paint correctly.

## Bugs caught and fixed during v0.2

These came up through real first-use feedback. Each is worth remembering as an editor invariant.

1. **`ConfigureAwait(false)` in commands** caused `Call from invalid thread` (carried from v0.1). UI commands must resume on the captured Avalonia `SynchronizationContext`. CA2007 suppressed project-wide for the editor with a comment explaining the UI-app context.
2. **Theme tokens not applied** (v0.1) — `App.OnFrameworkInitializationCompleted` calls `FluentDarkTheme.Default.Apply(this)` before any window opens.
3. **`Color` vs `IBrush` mismatch** (v0.1) — themes store `ImmutableSolidColorBrush` instances under `Phoenixml.Color.*` keys.
4. **`Theme.Apply(IResourceHost)` over-narrow cast** (v0.1) — accepts both `Application` and `StyledElement`.
5. **Status bar segment never updated** (v0.1) — `UpdateStatusBar` helper invoked from `OnActiveDocumentChanged`.
6. **Two title bars stacked** (v0.1) — `SystemDecorations="BorderOnly"` + `ExtendClientAreaChromeHints="NoChrome"`.
7. **Document tree label was full URI** (v0.1) — `XmlTreeNode.DocumentLabel` extracts just the filename.
8. **Single-line `utf-16`-declared serializer output** (v0.1 → fixed in Plan 7's `PhoenixmlDb.Core 1.0.30`).
9. **`RenameElementCommand.TryMergeWith` parameter direction** (v0.2 Plan 8) — `this` = already-applied command (holds the original snapshot), parameter = new command.
10. **PropertiesPane textbox didn't refresh on undo** (v0.2 first iteration) — the pane was removed entirely; F2 inline rename in tree replaced it.
11. **Element labels had redundant angle brackets** (v0.2) — `<catalog>` → `catalog`. Attributes keep `@id` as the disambiguator.
12. **Right-pane TextBox accepted typing but didn't sync to XDM** (v0.2) — set `IsReadOnly = true` to match expectations. Then dropped the text view entirely in favor of NodeListView.
13. **CLI file arg wasn't wired** (v0.2) — `desktop.Args` now flows through to `editor.OpenFileOnLaunch`.
14. **Auto-expand only 2 levels** caused empty-value-column on first load — bumped to 3, surfaces real values immediately.
15. **No visible column separator** — added 1-dip themed `Border`.
16. **Text-only elements duplicated their text** as child rows — `XmlTreeNode.HasChildren` returns false for text-only leaves; `XmlTreeNodeProvider.GetElementChildren` skips text-node children when no element children exist.

## Architectural validations

- **`IEditorCommand` + `IUndoStack` foundation works for real edits.** RenameElement and RenameAttribute share the same immutable-replace pattern (build new XDM node with changed property, swap in `XmlDocument._byId` under the same NodeId). NodeId stable means parent Children lists stay valid. Undo just swaps back to the stored previous snapshot.
- **`Document.Changed` event drives view refresh.** Both NodeListView (via XmlTreeNodeProvider.Changed) and the editor's status bar refresh on every edit. No manual repaint needed.
- **Foundation control composes for app needs.** NodeListView lives in `Phoenixml.Platform.Editing` and is reusable by future apps (Workbench's XSLT/XQuery editors). The editor app supplies the vocabulary providers and command routing.
- **CLI argument plumbing works through Avalonia's `IClassicDesktopStyleApplicationLifetime.Args`.** No manual argv parsing needed; `App.OnFrameworkInitializationCompleted` is the right hook.

## Rough-edges log (v0.3 backlog)

Ordered by friction.

### High friction

- [ ] **Value column is read-only.** You can see attribute values and text content but can't edit them. Next obvious feature: `SetAttributeValueCommand`, `SetTextContentCommand`. F2 in the value column would commit changes through the same IUndoStack pattern.
- [ ] **No way to insert or delete nodes.** Need `InsertElementCommand`, `InsertAttributeCommand`, `DeleteNodeCommand`. UX: right-click context menu on a tree row, or Insert/Delete keys. XmlNotepad uses the Insert menu + a small toolbar.
- [ ] **F2-then-immediately-type doesn't auto-focus the TextBox.** User has to click into the box. Avalonia container realization timing makes this finicky; document as polish item.
- [ ] **No address bar / file path indicator.** XmlNotepad has an editable address bar at the top. Useful for "open this other file" without going through the picker.

### Medium friction

- [ ] **No toolbar with Open/Save/Undo/Redo icons.** Menu + keyboard shortcuts cover the same ground but a toolbar matches XmlNotepad's UX.
- [ ] **No tabs for multiple documents.** Single-document only. `EditorState.ActiveDocument` is a single nullable field; opening a second file silently replaces the first.
- [ ] **No recent files menu.** `SettingsService` from Hosting is wired up but not consumed by the editor.
- [ ] **No problems pane / schema validation feedback.** `XmlSchemaValidator` and `ProblemsView` exist; the editor doesn't surface them.
- [ ] **No node-kind icons.** XmlNotepad shows a colored dot per node kind (red for namespace decls, blue for elements, etc.). `XmlTreeNode.IconKey` returns names like `"icon.xml.element"` but no icon assets render them.
- [ ] **Status bar is thin.** Only the document name appears. Encoding, line/col, schema-binding, XPath-of-selection are all spec'd in the platform design (§4.7) but unimplemented.
- [ ] **Name column is fixed at 360 dip.** Deeply nested attributes get pushed close to the column edge. Resizable splitter or dynamic column sizing would help.
- [ ] **No theme switching.** Dark only.

### Low friction (notice eventually)

- [ ] **No visual hint that text-only leaves with attributes still expand.** A user looking at `<price currency="USD">1299.99</price>` sees `price` with a chevron, value `1299.99`, but might not realize expanding shows `@currency`. A subtle "+" or "..." hint could help.
- [ ] **No keyboard navigation between rows during F2 edit.** Tab key would naturally move to the next editable cell once value-editing lands.
- [ ] **Mixed content (text interleaved with elements) shows text rows with quotes around the value.** Distinguishes from element labels but the styling could be more refined (italic? different color?).
- [ ] **XdmText labels still truncate at 40 chars with `…`.** Long text content gets cut off in the label even though the value column shows it in full. Consider truncating only when needed for layout.
- [ ] **No app icon** — taskbar shows the default Avalonia icon.
- [ ] **No first-launch registration** — gated on the phoenixml.net licensing backend per the licensing-API spec.
- [ ] **No virtualization beyond what `ListBox` does for free.** 100k-node documents not yet validated.

## What's next: Plan 9 candidates

In priority order:

1. **Value editing** — F2 in value column → `SetAttributeValueCommand` / `SetTextContentCommand`. Same architecture as rename.
2. **Insert / Delete commands** — context menu + Insert/Delete keys.
3. **Toolbar with Open/Save/Undo/Redo icons** — matches XmlNotepad.
4. **Address bar** at the top showing/editing the current file path.
5. **Tabs for multiple documents** — `EditorState` becomes a list, document picker in the chrome.
6. **Recent files** via `SettingsService`.
7. **Schema validation feedback** — wire `XmlSchemaValidator` → `ProblemsView`, click-to-navigate from problems to tree row.

## Validated environment

- OS: Linux Mint 22.3 (Cinnamon, X11)
- .NET: 10.0
- Avalonia: 11.2.4
- Phoenixml.Platform: 0.3.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.1.0 (in-repo)
- PhoenixmlDb.Core: 1.0.30 (NuGet)
