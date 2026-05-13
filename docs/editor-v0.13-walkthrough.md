# Phoenixml.Editor v0.13 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-13:** v0.13 ships the "Plan 23 polish pack" — eight quality-of-life improvements stacking on top of v0.12. Highlights: Copy XPath context menu, `last-action` status segment surfacing previously-silent rejects, MVP file-compare command, attribute cut/copy/paste, drag auto-scroll, breadcrumb ellipsization, XPath history (Up/Down), and XPath `fn:*` autocompletion popup (Tab to accept).

## How to run

```bash
cd /raid/elvogel/repos/phoenixml-platform
dotnet run --project src/Phoenixml.Editor --configuration Release -- \
  /raid/tmp/v0.8-smoke/data-and-schemas-by-type/data/library.xml
```

## What's new in v0.13

### Copy XPath (Plan 23 #1)

Right-click any tree row → **Copy XPath**. The canonical XPath (e.g. `/library/book[2]/title`) lands on the system clipboard as plain text. The `last-action` status segment confirms the copy with the XPath that was written.

### Failed-action status segment (Plan 23 #2)

A new `last-action` status segment surfaces previously-silent rejects:

- Cycle-rejected drag-drops (e.g. dragging a node onto its own descendant)
- Malformed-XML pastes
- Unsupported paste targets (e.g. pasting an attribute onto a non-element)
- Cut/Copy/Paste action confirmations

Auto-clears 4 seconds after the most recent notification. `LastActionStatus` is a generic notification primitive — future commands can call `NotifyAction(...)` on `EditorMainWindow` to surface status.

### Compare With… (Plan 23 #3, MVP)

**File → Compare With…** prompts for a second XML file, parses it, and shows a structural diff report in a modal window. Diff is keyed by canonical XPath — additions, removals, and changed attribute/shallow-text values are surfaced.

```
Compare: a.xml ↔ b.xml
2 difference(s).

− /r/y
+ /r/z
```

Order-of-children diffs are not surfaced separately in this MVP. A dual-pane side-by-side view with synced scrolling and inline change highlights is deferred to Plan 24.

### Attribute Cut / Copy / Paste (Plan 23 #4)

- Ctrl+C on an attribute row writes `name="value"` (entity-escaped) to the clipboard as plain text.
- Ctrl+V on an element row with attribute-format text on the clipboard runs the existing `InsertAttributeCommand` (single undo step).
- Ctrl+X on an attribute deletes it via the existing `DeleteNodeCommand`.
- Element subtree cut/copy/paste (v0.11) still works — paste auto-detects attribute-format text vs. XML.

### Drag auto-scroll (Plan 23 #5)

While dragging a tree row, hovering within 24 dip of the top or bottom edge of the tree viewport auto-scrolls. Scrolling is step-wise (~1 row per DragOver tick).

**Drop preview ghost** (rendering the dragged subtree shape near the cursor) is **deferred to Plan 24** — Avalonia's default drag visual is still a generic indicator. The dim-on-drag opacity feedback from Plan 19 remains.

### Breadcrumb ellipsization (Plan 23 #6)

When the ancestor chain exceeds 6 segments, the breadcrumb collapses to `seg1 › seg2 › … › seg-n-2 › seg-n-1 › seg-n`. The `…` placeholder is rendered as a regular button (so layout stays consistent) but clicking it is a silent no-op (its segment carries a null payload). Head/tail segments remain navigable.

### XPath history (Plan 23 #7)

The XPath query bar (Ctrl+G) tracks submitted expressions per session, deduplicated, capped at 50.

- **Up arrow** in the bar's input cycles to older expressions.
- **Down arrow** cycles back / clears the input.
- Submitting a new expression resets the cursor.
- History is **session-scoped** — not persisted across editor restarts in v0.13. Persistence is a Plan 24 candidate.

### XPath function autocompletion (Plan 23 #8, MVP)

Typing a word prefix (letters / digits / `-` / `_`) in the XPath bar opens a popup with matching `fn:*` function names (case-insensitive). Up/Down navigate the popup; Tab accepts the selection; Escape closes.

- No parameter hints, no context-sensitive filtering, no `map:` / `array:` / `xs:` namespaces yet — those are Plan 24.
- The popup's Up/Down arrows take priority over history cycling while the popup is open. History cycling resumes when the popup closes.

## Smoke-test checklist (manual)

| Action | Expected | Actual |
|---|---|---|
| Right-click a row → Copy XPath | Status bar: `Copied XPath: …`; clipboard contains the XPath. | tbd |
| Paste invalid XML via Ctrl+V | Status bar: `Paste failed: malformed XML …`. | tbd |
| Drag a row onto its own descendant | Status bar: `Move rejected: …`. | tbd |
| File → Compare With… → pick a doc | Modal report opens listing differences by XPath. | tbd |
| Compare identical docs | Report shows `0 difference(s).` | tbd |
| Select an attribute, Ctrl+C | Status bar: `Copied attribute @name`. | tbd |
| Select an element, Ctrl+V (attribute on clipboard) | Attribute added; single undo restores. | tbd |
| Drag near top of viewport | Tree auto-scrolls up. | tbd |
| Drag near bottom of viewport | Tree auto-scrolls down. | tbd |
| Navigate to a deep node (>6 ancestors) | Breadcrumb shows head/`…`/tail; head and tail clickable; `…` no-op. | tbd |
| Ctrl+G, type `//a`, Enter; type `//b`, Enter; press Up | Input shows `//b`; press Up again → `//a`. | tbd |
| Press Down from `//a` history | Returns to `//b`; Down again → empty. | tbd |
| Ctrl+G, type `coun` | Popup shows `count`. | tbd |
| Press Tab in popup | Selection inserted at caret; popup closes. | tbd |
| Press Escape with popup open | Popup closes; input keeps content. | tbd |

## Rough-edges log (v0.14 backlog)

### Resolved 2026-05-13 (Plan 23)

- [x] ~~No right-click "Copy XPath" on tree rows.~~
- [x] ~~Silent rejects with no UI feedback.~~
- [x] ~~No file-compare action.~~ (MVP textual report)
- [x] ~~No cut/copy/paste of attributes.~~
- [x] ~~No drag auto-scroll.~~ (drop preview ghost deferred)
- [x] ~~Breadcrumb cannot ellipsize for deep trees.~~
- [x] ~~No XPath history.~~
- [x] ~~No XPath function autocompletion.~~ (MVP `fn:*` prefix popup)

### High friction (carried)

- [ ] **Dual-pane side-by-side compare view** with synced scrolling and inline change highlights. v0.13 ships a textual diff report only.
- [ ] **Drop preview ghost** rendering the dragged subtree near the cursor. v0.13 has auto-scroll but no ghost.
- [ ] **Persistent XPath history** across editor restarts (currently per-session).
- [ ] **Parameter hints + signature info** in XPath autocompletion.
- [ ] **Context-aware autocompletion** (axis names, element names from the active doc).
- [ ] **`map:` / `array:` / `xs:` namespace autocompletion.**

### Medium / low (carried from v0.12)

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
- Material.Icons.Avalonia: 2.x
- Phoenixml.Platform: 0.13.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.9.0 (in-repo)
- PhoenixmlDb.Core: 1.1.2 (NuGet)
- PhoenixmlDb.XQuery: 1.3.6 (NuGet)
- PhoenixmlDb.Xslt: 1.3.10 (CPM pinned, not referenced)
