# Phoenixml.Editor v0.12 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-13:** v0.12 ships **Drag-and-drop reorder** of tree rows (Plan 19) and **Breadcrumb + status-bar XPath** for the current selection (Plan 20). Drag any element row onto another to move it; drop **on** the row inserts as last child, drop **above** or **below** inserts as a preceding or following sibling. NodeId identity is preserved across the move, so selection and external references survive. Single undo per drag gesture. Selecting any node now shows its canonical XPath in the status bar and renders a clickable ancestor breadcrumb above the tree.

## How to run

```bash
cd /raid/elvogel/repos/phoenixml-platform
dotnet run --project src/Phoenixml.Editor --configuration Release -- \
  /raid/tmp/v0.8-smoke/data-and-schemas-by-type/data/library.xml
```

## What works in v0.12

### Drag-and-drop reorder (Plan 19) — NEW

- **Drag any element row** by clicking and holding the left mouse button (4-dip movement threshold discriminates click vs drag). The dragged row dims to 0.5 opacity for the duration of the drag.
- **Drop targets** show a 2-dip blue border indicating where the drop will land:
  - **Top third** of a row → preceding sibling (top-edge border).
  - **Middle** of a row → last child of the target element (full border).
  - **Bottom third** of a row → following sibling (bottom-edge border).
- **Re-parenting** is allowed within the same document — drag a `<book>` from one `<library>` to another, or into a `<series>` element.
- **Cycle prevention** — dropping a row onto itself or any of its descendants is rejected silently at the command layer.
- **Single undo step** — one Ctrl+Z reverses the move, regardless of subtree size. Both old and new parents are restored from snapshots; the source's `Parent` pointer reverts too.
- **NodeId-preserving** — the moved subtree is the same identity, not a copy. Selection on the dragged element survives the move; external references (validation diagnostics, outline) remain valid.

### Carried from v0.11

- Cut / Copy / Paste subtree (Ctrl+X / Ctrl+C / Ctrl+V) with cross-document and cross-application support
- Schema-aware Insert (submenus from XSD, auto-value-edit for simple types)
- Find / Replace (Ctrl+F, Ctrl+H)
- Schema management cascade
- Inline validation glyphs, click-to-navigate, splitter, save-on-quit
- Compound undo

### Architecture pieces added in v0.12

- **`MoveSubtreeCommand`** in `Phoenixml.Platform.Editor.Xml.Editing` — identity-preserving subtree move. Apply captures snapshots of the source's old parent + target parent for Revert, updates the source's `Parent` pointer (cross-parent moves only), then splices the source out of the old parent's `Children` and into the target parent's `Children` at the requested index. Same-parent reorder uses a single `ReplaceNode` with adjusted insert-index math.
- **`DropPosition`** enum in `Phoenixml.Platform.Abstractions.Editing` — three positions (`AsLastChild`, `BeforeTarget`, `AfterTarget`) computed from the cursor's vertical position within the drop-target row.
- **`NodeListView` drag-drop wiring** — left-button `PointerPressed` + `PointerMoved` track drag candidates with a 4-dip threshold; `DragDrop.DoDragDrop` carries the source's `NodeId` via a custom `application/x-phoenixml-nodeid` DataObject format; `DragOver` / `DragLeave` / `Drop` handlers paint the drop indicator and raise `NodeReorderRequested(source, target, position)`.
- **`EditorMainWindow` handler** — translates `NodeReorderRequested` into a `MoveSubtreeCommand` against the active `XmlDocument`. `AsLastChild` routes to the target element directly; `BeforeTarget` / `AfterTarget` look up the target's parent and compute the insert index from the target's position in its parent's `Children` list.

## Smoke-test checklist (manual)

Load `/raid/tmp/v0.8-smoke/data-and-schemas-by-type/data/library.xml`. Test the following:

| Action | Expected | Actual |
|---|---|---|
| Drag a `<book>` onto the middle of another `<book>` | Drops on row → becomes last child of target book. | tbd |
| Drag a `<book>` to the top third of another `<book>` | Becomes the immediate preceding sibling. | tbd |
| Drag a `<book>` to the bottom third of another `<book>` | Becomes the immediate following sibling. | tbd |
| Drag a `<book>` onto itself | No drop (cycle prevention); cursor indicates "not allowed". | tbd |
| Drag a `<book>` onto its own `<title>` descendant | No drop (cycle); silent reject. | tbd |
| Drag a `<title>` onto its own parent `<book>` (middle) | `<title>` moves to last child position of that book. | tbd |
| Ctrl+Z after any drop | Move reverses in one step. | tbd |
| Cross-`<library>` move (if multiple) | Source removed from old library; appears in new library. | tbd |
| Click + release without 4-dip movement on a row | Normal selection — no drag triggered. | tbd |
| Click chevron during a drag-eligible row | Expand/collapse fires; no drag. | tbd |
| F2 / value-cell edit during selection | Drag detection skipped while a row is in edit mode. | tbd |

## Rough-edges log (v0.13 backlog)

### Resolved 2026-05-12 (Plan 19)

- [x] ~~**No drag-and-drop reorder of children.**~~ — Drag a row, drop on/above/below another to move. NodeId-preserving, single undo, cycle-rejected.

### High friction (carried)

- [ ] **No XPath query bar.**
- [ ] **No regex search/replace.**
- [ ] **No outline/breadcrumb showing the selected node's full XPath.**
- [ ] **No compare-two-documents view.**
- [ ] **Failed-replace / failed-paste / failed-drop reporting** — invalid XML names, malformed clipboard XML, cycle-rejected drops — all currently silent. Need a status-bar note.
- [ ] **Cut / copy / paste of attributes.**
- [ ] **Paste-as-sibling** (previous / next) — positional variants of Ctrl+V.

### Drag-drop follow-ups (deferred from Plan 19)

- [ ] **Cross-document drag** — drag a row from one tab/doc to another. Needs serialize-then-paste path; NodeId spaces differ between docs. Plan 22.
- [ ] **Copy-on-drag** (Ctrl+drag) — replicate instead of move.
- [ ] **Multi-row drag** — needs multi-select first.
- [ ] **Drop onto attributes / text nodes** — currently rejected at the handler level; could allow drop-on-element-of-attribute-row as a shortcut.
- [ ] **Drop scrolling** — long trees: dragging near the top/bottom edge of the viewport should auto-scroll. Currently the user must scroll manually before starting the drag.
- [ ] **Drop preview / ghost** — Avalonia's default drag cursor is a generic indicator; XmlNotepad shows the dragged element name beside the cursor.

### Medium / low (carried)

- [ ] **No multi-namespace schema support.**
- [ ] **No type-aware default values for schema-aware Insert.**
- [ ] **Documentation popups from `xs:annotation`.**
- [ ] **Right-click "Apply Schema to..." in the explorer.**
- [ ] **LSP refresh / editor LSP-client integration.**
- [ ] **No file-system watcher in the explorer.**
- [ ] **No node-kind icons.**
- [ ] **No theme switching.**
- [ ] **Editor doesn't accept a folder as a CLI arg.**
- [ ] **GridSplitter drag-to-zero.**
- [ ] **Recent Files entries with missing files are kept.**
- [ ] **SubtreeSerializer is duplicated work vs XmlSerializer.**

## Plan 20 — Breadcrumb + status-bar XPath (NEW)

Read-only navigation feature; no document mutation.

- **Status bar XPath segment** — selecting any tree row updates the right-most
  status-bar segment with the node's canonical XPath (e.g.
  `/library/book[2]/title` or `/library/book[1]/@isbn`). Empty when no
  selection.
- **Breadcrumb above the tree** — the existing `BreadcrumbView` between the
  find bar and the node list now renders one clickable button per ancestor
  step. The leading `/` segment represents the document root. Clicking any
  segment navigates the tree via `EnsureVisibleAndSelect`, expanding
  collapsed ancestors on the way.
- **Predicate convention** — positional predicates (`book[2]`) are emitted
  only when the element has at least one same-name sibling. Single
  occurrences render without `[1]` for cleaner UI (matches XML Notepad).
- **Non-element steps** — attributes show as `@name`; text/comment/PI
  nodes show as `text()` / `comment()` / `processing-instruction('target')`.
- **Tab-switch safety** — `OnActiveDocumentChanged` clears both the XPath
  segment and the breadcrumb so stale selection state never bleeds across
  documents.

### Architecture pieces added in Plan 20

- **`XPathBuilder`** in `Phoenixml.Platform.Editor.Xml.Navigation` —
  static `Build(XmlDocument, NodeId) → string`. Walks up via
  `XdmNode.Parent`, counts same-name siblings for positional predicates,
  returns empty string for unresolved ids. Covered by 11 unit tests.
- **`EditorMainWindow.OnNodeListSelectionChanged`** — single point of
  truth that recomputes the XPath, rebuilds the breadcrumb's segment list
  (each segment's `Payload` is the ancestor's `NodeId` string), and
  triggers `UpdateStatusBar`.

### Smoke-test checklist (manual)

Load `/raid/tmp/v0.8-smoke/data-and-schemas-by-type/data/library.xml`. Test:

| Action | Expected | Actual |
|---|---|---|
| Select root `<library>` | Status bar shows `/library`; breadcrumb has `/` › `library`. | tbd |
| Select first `<book>` (one of many) | Status bar shows `/library/book[1]`; breadcrumb has `/` › `library` › `book[1]`. | tbd |
| Select an `<isbn>` attribute on the second book | Status bar shows `/library/book[2]/@isbn`; breadcrumb terminates with `@isbn`. | tbd |
| Select a text node inside `<title>` | Status bar shows `/.../title/text()`. | tbd |
| Click a parent breadcrumb segment | Tree selection moves to that ancestor; collapsed ancestors expand. | tbd |
| Click the leading `/` segment | Selection lands on the document root element. | tbd |
| Switch tabs to a different document | Breadcrumb empties + XPath segment disappears, then repopulates for the new tab's first selection. | tbd |
| Delete the selected node | Breadcrumb + XPath clear (no SelectionChanged → manual: tap a sibling row to verify rebuild). | tbd |

## What's next: Plan 21 candidates

In priority order:

1. **Editable XPath bar** — type an XPath, navigate or select-set the matches.
2. **Right-click "Copy XPath" on tree rows** — clipboard convenience.
3. **Failed-* reporting** — status-bar segment surfacing silent rejects (failed paste, cycle-rejected drop, malformed replace).
4. **Compare two documents** — diff side-by-side using two NodeListView instances.
5. **Cut/copy/paste of attributes.**
6. **Auto-scroll during drag** + **drop preview ghost** — drag-drop polish.
7. **Breadcrumb ellipsization** for deeply nested docs.

LSP integration still waits on engine release cadence.

## Validated environment

- OS: Linux Mint 22.3 (Cinnamon, X11)
- .NET: 10.0
- Avalonia: 11.2.4
- Material.Icons.Avalonia: 2.x
- Phoenixml.Platform: 0.12.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.8.0 (in-repo)
- PhoenixmlDb.Core: 1.1.0 (NuGet)
- PhoenixmlDb.XQuery: 1.3.1 (NuGet)
