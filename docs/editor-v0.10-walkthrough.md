# Phoenixml.Editor v0.10 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-12:** v0.10 ships **schema-aware Insert** (Plan 17) — the biggest productivity win in the XML Notepad parity track. Right-click an element when a schema is bound, and instead of getting a generic `<newElement>` placeholder you get a submenu of valid child element names from the XSD content model. Pick one, and for simple-typed elements (`xs:string`, `xs:int`, `xs:gYear`, complex-with-text-only) the cursor jumps directly into the value cell.

## How to run

```bash
cd /raid/elvogel/repos/phoenixml-platform
dotnet run --project src/Phoenixml.Editor --configuration Release -- \
  /raid/tmp/v0.8-smoke/data-and-schemas-by-type/data/library.xml
```

## What works in v0.10

### Schema-aware Insert (Plan 17) — NEW

When the active document has a schema bound (via auto-detect, xsi: hint, or per-doc override):

- **Right-click `<library>` → Insert Child Element →** submenu shows `book` (the only valid child per the XSD).
- **Right-click `<book>` → Insert Child Element →** submenu shows `title`, `author`, `year` in XSD declaration order.
- **Right-click `<book>` → Insert Attribute →** submenu shows `isbn` (declared, not yet present on the element).
- Pick `title` from the submenu → `<title></title>` inserted **and cursor auto-jumps into the value cell** because title is `xs:string` (simple-typed). Type content, Enter commits.
- Pick `book` from a `<library>` submenu → `<book>` inserted, row selected (no auto-value-edit; book is complex).
- Pick `isbn` from the Insert Attribute submenu → new `@isbn=""` inserted, cursor in the value cell (attributes always auto-value-edit).

When the doc has no schema bound (or schema-aware options come back empty for the element):

- "Insert Child Element" and "Insert Attribute" fall back to the existing **direct-click** behavior — generic `newElement` / `newAttr` placeholder + F2-rename mode.

### Carried from v0.9

- Find / Replace (Ctrl+F, Ctrl+H)
- Schema management cascade (auto-detect across 3 folder layouts, smart-root, cache, picker, reload-on-discard)
- Inline validation glyphs, click-to-navigate, splitter, save-on-quit
- Compound undo (Replace All in one Ctrl+Z)

### Architecture pieces added in v0.10

- **`SchemaElementResolver`** in `Phoenixml.Platform.Editor.Xml.Validation` — top-down walk from doc root using `XmlSchemaSet.GlobalElements` for the root + recursive content-particle traversal (`xs:sequence`, `xs:choice`, `xs:all`) for nested elements. Returns the `XmlSchemaElement` declaration for any in-memory XdmElement, or null.
- **`SchemaInsertOptionsService`** in the same namespace — given `(doc, parent NodeId, schemas)` returns `SchemaInsertOptions(ChildElementNames, AttributeNames)` ordered by XSD declaration. Attribute list excludes those already present on the element.
- **`InsertOptions`** mutable class in `Phoenixml.Platform.Abstractions.Editing` — the exchange shape between NodeListView and host.
- **`NodeListView.BuildingInsertOptions`** event — fires when the right-click ContextMenu is about to open; host populates `InsertOptions`.
- **`NodeListView.NodeInsertChildElementWithNameRequested` / `NodeInsertAttributeWithNameRequested`** events — fire when the user picks a submenu item; carry the chosen name.
- **`NodeListView.BeginValueEditById`** — mirror of `BeginRenameById` that enters value-edit mode; powers the auto-focus into value cell for simple-typed inserts.

## Bugs caught and fixed during v0.10

1. **Header-text lookup of MenuItems failed silently.** First version of `RowContextMenu_Opening` located the Insert items by comparing `mi.Header is string h && h == "Insert Child Element"`. Avalonia 11 wraps menu headers in a Control (e.g., `AccessText`) for accessibility, so the string cast returned false, `insertChildItem` stayed null, `ConfigureInsertItem` never ran, and the menu fell back to the AXAML direct-click default. Fixed by index-based lookup matching the AXAML order (`menu.Items[0]` = InsertChild, `[1]` = InsertAttribute, `[2]` = Separator, `[3]` = Delete).

2. **ContextMenu bindings don't resolve at Opening time.** Diagnostic logging confirmed `menu.DataContext == null` and `mi.Tag == null` when the Opening event fires — Avalonia ContextMenus live in a detached popup tree, and the bindings only settle AFTER the popup opens (which is why direct-click handlers can read `mi.Tag` successfully). The Opening event runs too early. Fixed by adding an upstream `PointerPressed` handler on the NodeListView that hit-tests for the `ListBoxItem` ancestor on right-click and stashes its `DataContext` (the `NodeRowViewModel`); the Opening handler reads from the stash. Tunnel+Bubble + `handledEventsToo` registration ensures the pointer event reaches us regardless of inner-control dispatch.

## Architectural validations

- **Diagnostics before iteration is the right discipline.** Plan 17 echoed the right-click investigation from earlier — I spent multiple rounds guessing about Avalonia binding behavior before adding `Console.WriteLine` to the Opening handler. The diagnostics revealed in one run what speculation didn't in three: `menu.DataContext` is null at Opening time, full stop. Adding instrumentation as the FIRST iteration on UI-framework events would have saved time.
- **Two-level abstraction (resolver + service) keeps the schema side clean.** `SchemaElementResolver` knows nothing about insertion; `SchemaInsertOptionsService` consumes it for one specific purpose. Future features (schema-driven validation hints, type-aware default values, completion in a future LSP-client path) can reuse `SchemaElementResolver` without touching the service.
- **`BuildingInsertOptions` is the third "host populates this" hook pattern** alongside Plan 13's `DiagnosticIndex` and Plan 15's `MatchIndex`. All three keep the foundation control vocabulary-agnostic: NodeListView fires an event; the editor side knows how to read XSDs / run validation / search. The pattern generalizes — future features like "context-menu actions" or "drag targets" can follow the same shape.
- **Stash-on-PointerPressed pattern is the workaround for Avalonia popup bindings.** Worth carrying as a known idiom for any future foundation control that needs to discriminate by source row at popup-open time.

## Smoke-test results (2026-05-12)

Loaded `library.xml` with `library.xsd` auto-detected (Layout B: data/ + schemas/).

| Action | Result |
|---|---|
| Right-click `<library>` → Insert Child Element | Submenu shows `book`. |
| Pick `book` | New `<book>` appears as child; selected. No auto-value-edit (book is complex). |
| Right-click new `<book>` → Insert Child Element | Submenu shows `title`, `author`, `year`. |
| Pick `title` | New `<title></title>` inserted. Cursor in value cell. Type "Programming Pearls", Enter — commits. |
| Right-click `<book>` → Insert Attribute | Submenu shows `isbn`. |
| Pick `isbn` | New `@isbn=""` inserted. Cursor in value cell. Type ISBN, Enter — commits. "missing isbn" validation error clears after debounce. |
| Right-click `<title>` (simple type) | "Insert Child Element" has no submenu — direct-click only (falls back to generic `newElement`). |
| Open a no-schema doc | All menus revert to generic single-click. Inserts `newElement` / `newAttr` with F2 rename mode. |
| Validate other features | Find/Replace, click-to-navigate, undo/redo all unaffected. |

## Rough-edges log (v0.11 backlog)

### Resolved 2026-05-12 (Plan 17)

- [x] ~~**No schema-aware Insert suggestions.**~~ Right-click → submenu of valid children + declared attributes from the XSD. Auto-value-edit for simple-typed elements.

### High friction

- [ ] **No XPath query bar** — different mental model than substring Find (filter tree to matching node-set vs. iterate matches). Big productivity win for power users.
- [ ] **No regex search/replace.**
- [ ] **No cut/copy/paste subtree (Ctrl+X/C/V).**
- [ ] **No drag-and-drop reorder of children.**
- [ ] **No outline/breadcrumb showing the selected node's full XPath.**
- [ ] **No compare-two-documents view.**
- [ ] **Failed-replace reporting** (Plan 16 polish — invalid XML names after substring replacement fail silently).
- [ ] **No type-aware default values** — inserting `<year>` could default to current year; `<dateTime>` to now. Plan 18 polish.

### Medium friction

- [ ] **No multi-namespace schema support** — only no-namespace schemas resolve. Plan 18+.
- [ ] **No substitution groups / xs:group ref / xs:any wildcards** in the suggestion list. Plan 18+.
- [ ] **Documentation popups from `xs:annotation`** — hover a menu item to see XSD docs.
- [ ] **Attribute value enumerations from `xs:enumeration` facets** — show a dropdown when the schema says the value must be one of N.
- [ ] **Right-click "Apply Schema to..." in the explorer** — still WIP from v0.8.
- [ ] **LSP refresh / editor LSP-client integration** — still blocked on engine release cadence.
- [ ] **No file-system watcher in the explorer.**
- [ ] **No node-kind icons.**
- [ ] **No theme switching.**

### Low friction

- [ ] **Editor doesn't accept a folder as a CLI arg.**
- [ ] **GridSplitter drag-to-zero** leaves explorer essentially invisible.
- [ ] **Mutation helpers copy original positions to the replacement node** — misleading after many edits.
- [ ] **Recent Files entries with missing files are kept.**

## What's next: Plan 18 candidates

Per the XML Notepad parity backlog, in priority order:

1. **Cut / Copy / Paste subtree** (Ctrl+X/C/V) — clipboard API + existing CompoundCommand for the multi-step paste.
2. **XPath query bar** — filter tree to matching node-set; click to navigate.
3. **Outline / breadcrumb on selection** — selected node's full XPath in status bar + breadcrumb above tree.
4. **Drag-and-drop reorder.**
5. **Compare two documents.**
6. **Schema-aware polish**: type-aware default values, xs:enumeration dropdowns, xs:annotation documentation popups.
7. **Failed-replace reporting** (Plan 16 polish).

LSP integration still waits on engine release cadence (XQuery 1.3.2 / Xslt 1.3.3 publishing).

## Validated environment

- OS: Linux Mint 22.3 (Cinnamon, X11)
- .NET: 10.0
- Avalonia: 11.2.4
- Material.Icons.Avalonia: 2.x
- Phoenixml.Platform: 0.10.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.6.0 (in-repo)
- PhoenixmlDb.Core: 1.1.0 (NuGet)
- PhoenixmlDb.XQuery: 1.3.1 (NuGet)
