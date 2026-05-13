# Phoenixml.Editor v0.11 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-12:** v0.11 ships **Cut / Copy / Paste of element subtrees** (Plan 18). Ctrl+C serializes the selected element + descendants to the system clipboard; Ctrl+X same + delete; Ctrl+V parses clipboard XML and pastes as a child of the selected element. Cross-document and cross-application paste both work for free.

## How to run

```bash
cd /raid/elvogel/repos/phoenixml-platform
dotnet run --project src/Phoenixml.Editor --configuration Release -- \
  /raid/tmp/v0.8-smoke/data-and-schemas-by-type/data/library.xml
```

## What works in v0.11

### Cut / Copy / Paste subtree (Plan 18) — NEW

- **Ctrl+C** on a selected element row → serializes the element + descendants to a standalone XML string (no `<?xml?>` declaration), writes to clipboard as both `application/xml` and `text/plain`.
- **Ctrl+X** on a selected element row → same as Ctrl+C, then runs `DeleteNodeCommand`. Clipboard write isn't undoable; one Ctrl+Z restores the cut node.
- **Ctrl+V** on a selected element row → reads XML from clipboard (`application/xml` first, falls back to `text/plain`), parses, inserts as the last child of the selected element via `InsertSubtreeCommand`. Single undo step regardless of subtree size.
- **Cross-document paste** works — copy from doc A, switch tabs/files via the explorer, paste into doc B. The parser allocates fresh NodeIds in the target document; the clipboard payload is plain XML.
- **Cross-application paste** works — paste XML copied from any tool (browser, terminal, other editor). Malformed XML silently no-ops (Plan 19 will add a status-bar note).
- **Nested-paste** (pasting a `<library>` inside the original `<library>`) works; the pasted subtree's `Parent` pointer is set correctly so subsequent Delete works without surprises.

### Carried from v0.10

- Schema-aware Insert (submenus from XSD, auto-value-edit for simple types)
- Find / Replace (Ctrl+F, Ctrl+H)
- Schema management cascade (auto-detect, smart-root, cache, picker, reload-on-discard)
- Inline validation glyphs, click-to-navigate, splitter, save-on-quit
- Compound undo (Replace All in one Ctrl+Z)

### Architecture pieces added in v0.11

- **`SubtreeSerializer`** in `Phoenixml.Platform.Editor.Xml.Clipboard` — given an `XdmElement` + descendants, emits standalone XML with `OmitXmlDeclaration=true` and synthesized `xmlns:` declarations for any namespace prefixes used in the subtree.
- **`XmlDocument.ParseSubtreeXml(string)`** (internal) — parses an XML string into the current document. Allocates fresh NodeIds via `AllocateNodeId`, registers each node via `AddNode`, merges namespace bindings into the target's namespace table (URI-deduped so cross-document pastes don't bloat the namespace store).
- **`InsertSubtreeCommand`** in `Editing` — wraps `ParseSubtreeXml` + splice into a single `IEditorCommand`. Same Revert/redo-orphan pattern as `DeleteNodeCommand`; `ReferencedNodeIds` keeps the pasted subtree alive across Compact-on-Save while the command is on the undo stack.
- **`ClipboardService`** in `Phoenixml.Editor.Clipboard` — wraps Avalonia's `IClipboard` (via an `IClipboardBackend` seam since `IClipboard` is `[NotClientImplementable]`). Methods: `WriteXmlAsync`, `ReadXmlAsync`. The `Backend` seam lets tests stub the clipboard without fighting Avalonia internals.

## Bugs caught and fixed during v0.11

1. **`IClipboard` is `[NotClientImplementable]`.** Avalonia 11 marks the interface so direct stub implementations produce `CS0535` compile errors. Solution: introduce an `IClipboardBackend` interface (3 methods: `SetDataObjectAsync`, `GetDataAsync`, `GetTextAsync`) that the `ClipboardService` consumes. `AvaloniaClipboardBackend` bridges the real `IClipboard` for production; tests implement `IClipboardBackend` directly with an in-memory dictionary.
2. **Pasted subtree root's `Parent` pointed at the parser's synthetic document, not the target parent.** Caught during the v0.11 smoke test — copy `<library>` and paste under `<library>` gave a nested structure, but right-click → Delete on the inner library did nothing (DeleteNodeCommand throws "Parent is not an XdmElement" because Parent was the synthetic doc). Undo worked because it restores the parent's Children list without walking Parent. Fix: after parsing, replace the subtree root with a clone whose `Parent = _parentId` via a new `WithParent` helper (mirrors the `WithChildren` pattern). Regression test `InsertedSubtreeRootHasCorrectParentPointer` verifies both Parent assignment AND that a subsequent DeleteNodeCommand on the pasted root succeeds.
3. **XmlSerializer in NuGet, not in this repo.** Plan 18 sketched "refactor XmlSerializer to expose WriteElement". `XmlSerializer` is in `PhoenixmlDb.Core` (NuGet 1.1.0) — we can't modify it here. Fallback: SubtreeSerializer has its own recursive walk producing the same output shape. Acceptable for v0.11; future improvement could move SubtreeSerializer to Core and consolidate.

## Architectural validations

- **The orphan-in-_byId + Revert pattern generalizes.** `DeleteNodeCommand`, `InsertElementCommand`, `InsertAttributeCommand`, and now `InsertSubtreeCommand` all share the shape: mutate `parent.Children` via `ReplaceNode`, leave nodes in `_byId` after Revert so redo can re-splice without rebuilding. `ReferencedNodeIds` keeps the orphans alive across Compact-on-Save. Future commands that need atomicity (drag-reorder = delete + insert pair) get this for free.
- **Cross-document paste falls out of the design.** Clipboard payload is plain XML text; the target document's parser allocates fresh NodeIds. No special inter-doc plumbing required.
- **Cross-application paste also works.** XML on the clipboard from any source (browser, terminal, other editor) parses successfully if well-formed. Plan 18's `text/plain` fallback covers the case where the source app doesn't set `application/xml`.
- **The `IClipboardBackend` seam is a reusable pattern.** Any Avalonia interface marked `[NotClientImplementable]` can be wrapped this way: define a minimal internal seam that mirrors only the methods we need, implement a thin bridge for production, stub the seam in tests.

## Smoke-test results (2026-05-12)

Loaded `library.xml`. Tested the full clipboard flow:

| Action | Result |
|---|---|
| Click `<book>` + Ctrl+C | XML copied to clipboard. Verifiable via external paste (browser, terminal). |
| Click `<library>` + Ctrl+V | New `<book>` appears at end of library's children. Same attributes/descendants as source. |
| Ctrl+Z after paste | Pasted subtree removed in one undo step. |
| Ctrl+X on a `<book>` | Book disappears (DeleteNodeCommand fires). Clipboard still has XML. |
| Ctrl+V after Cut | Cut book reappears at end of children. |
| Cross-doc paste | Copy from library.xml → switch to catalog.xml → paste → subtree arrives (likely schema-invalid against catalog's schema; glyphs reflect that, correct behavior). |
| Cross-app paste | `echo '<note>hi</note>' \| xclip -selection clipboard -i` then Ctrl+V → new `<note>` appears. |
| Malformed paste | Random plain text via Ctrl+V → no-op, no crash. |
| Nested paste + delete | Copy `<library>`, paste under itself, right-click inner library → Delete → works. (Pre-fix: was silently no-op due to Parent-pointer bug.) |

## Rough-edges log (v0.12 backlog)

### Resolved 2026-05-12 (Plan 18)

- [x] ~~**No Cut / Copy / Paste subtree**~~ — Ctrl+X/C/V wired end-to-end with cross-doc + cross-app support.

### High friction

- [ ] **No XPath query bar** — different mental model than substring Find.
- [ ] **No regex search/replace.**
- [ ] **No drag-and-drop reorder of children.**
- [ ] **No outline/breadcrumb showing the selected node's full XPath.**
- [ ] **No compare-two-documents view.**
- [ ] **Failed-replace / failed-paste reporting** — invalid XML names after substring replace, malformed clipboard XML on paste, schema-invalid pasted subtree — all currently silent. Need a status-bar note or one-line problem counter.
- [ ] **Cut / copy / paste of attributes** — distinct command shapes from elements.
- [ ] **Paste-as-sibling** (previous / next) — positional variants.

### Medium friction

- [ ] **No multi-namespace schema support** — only no-namespace XSDs resolve.
- [ ] **No type-aware default values for schema-aware Insert.**
- [ ] **Documentation popups from `xs:annotation`.**
- [ ] **Right-click "Apply Schema to..." in the explorer** — still WIP from v0.8.
- [ ] **LSP refresh / editor LSP-client integration** — still blocked on engine release cadence.
- [ ] **No file-system watcher in the explorer.**
- [ ] **No node-kind icons.**
- [ ] **No theme switching.**

### Low friction

- [ ] **Editor doesn't accept a folder as a CLI arg.**
- [ ] **GridSplitter drag-to-zero** leaves explorer essentially invisible.
- [ ] **Recent Files entries with missing files are kept.**
- [ ] **SubtreeSerializer is duplicated work vs XmlSerializer.** If we move it into PhoenixmlDb.Core (next NuGet bump), the duplication goes away.

## What's next: Plan 19 candidates

In priority order:

1. **Drag-and-drop reorder** — reuses InsertSubtreeCommand + DeleteNodeCommand (or a new MoveSubtreeCommand wrapping both as a CompoundCommand for single undo). The drag part is Avalonia DragDrop primitives.
2. **Outline / breadcrumb on selection** — selected node's full XPath in status bar + clickable breadcrumb above the tree. Pure read-side; no mutation.
3. **XPath query bar** — filter tree to matching node-set. Different mental model than Find; new control above the tree.
4. **Failed-paste / failed-replace reporting** — status-bar segment that surfaces count of silent failures, click to see details.
5. **Compare two documents** — diff side-by-side using two NodeListView instances.
6. **Cut/copy/paste of attributes** — small extension to v0.11's element scope.

LSP integration still waits on engine release cadence.

## Validated environment

- OS: Linux Mint 22.3 (Cinnamon, X11)
- .NET: 10.0
- Avalonia: 11.2.4
- Material.Icons.Avalonia: 2.x
- Phoenixml.Platform: 0.11.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.7.0 (in-repo)
- PhoenixmlDb.Core: 1.1.0 (NuGet)
- PhoenixmlDb.XQuery: 1.3.1 (NuGet)
