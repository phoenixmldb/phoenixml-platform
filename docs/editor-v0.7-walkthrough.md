# Phoenixml.Editor v0.7 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-09:** v0.7 ships precise diagnostic→node mapping. Click any error in ProblemsView and the editor selects the *exact* offending node (no more "first-by-name" regex fallback). Inline ✕ glyphs land on the rows that actually have errors. Built on Plan 13's `XdmNode.SourceLine` / `SourceColumn` (Core 1.1.0) and a push-based rewrite of `XmlSchemaValidator` that drives `System.Xml.Schema.XmlSchemaValidator` directly off our `XdmDocument` walk — no serialize-and-reparse round-trip.

## How to run

```bash
cd /raid/elvogel/repos/phoenixml-platform
dotnet run --project src/Phoenixml.Editor --configuration Release -- /path/to/file.xml
```

Demo fixture used during smoke-testing: `/raid/tmp/v0.6-smoke/library.xml` (two `<book>` entries, second is missing `@isbn` and has `<year>not-a-year</year>`; XSD declares `@isbn` required and `<year>` typed as `xs:gYear`).

## What works in v0.7

### Precise diagnostic→node mapping (NEW)

- Validator runs as a push-based walk over the `XdmDocument` — `ValidateElement` / `ValidateAttribute` / `ValidateText` / `ValidateEndElement` called explicitly. Current `XdmNode` is in scope when each call returns; the diagnostic event handler captures it directly. **No line/col arithmetic, no serialization round-trip.**
- `IDiagnostic.Location` is now `DiagnosticLocation(NodeId, Line, Column)` and is populated for every diagnostic that fired during the walk (line/col come from the offending node's parse-time `SourceLine` / `SourceColumn`).
- **Click-to-navigate** in ProblemsView selects the exact NodeId: `EnsureVisibleAndSelect` walks the provider tree, expands every ancestor on the path, rebuilds the flat-row list, then selects. Works even from a fully-collapsed tree.
- **Inline error overlays** — ✕ red / ⚠ yellow Unicode glyph in front of the row label whenever `DiagnosticIndex` has a severity for that NodeId. Updates automatically on every validation pass via `provider.TriggerRefresh()`.

### Source positions on XdmNode (Core 1.1.0)

- `XdmNode.SourceLine` and `XdmNode.SourceColumn` (1-based; 0 = no source position).
- Populated by `XmlDocumentParser` from `IXmlLineInfo` for elements, attributes, text, comments, and PIs.
- Synthesized nodes (editor's Insert commands) get default 0/0.
- Round-trip via `Save → Reparse` resets positions to whatever the new parse sees (the serialized output's layout, not the original input's). Mutation helpers like `RenameElementCommand` copy original positions to the replacement node — logically still "at the same source location" until the next save+reparse.

### Carried from v0.6

Schema validation pipeline (Plan 12), GridSplitter, save-on-quit dialog, inline value editing, structural edits, explorer, recent files, address bar.

## Bugs caught and fixed during v0.7

1. **Validator's line/col was in the serialized output, not the original file.** Plan 13 Task B3's first implementation used `FindNodeNearLine(doc, line, col)` against parse-time positions — but the validator round-trips through serialization first. Even on a freshly-loaded document, multi-line attribute formatting in the source vs. single-line in the serialized output meant lines didn't align. Diagnostic mapping landed on the wrong nodes (first `<year>1978</year>` on line 7 instead of second `<year>not-a-year</year>` on line 12).
2. **Click-to-navigate no-op'd on collapsed trees.** `SelectById` only walked the visible (`_flatRows`) list. Nodes inside collapsed parents were unreachable. Fixed via `NodeListView.EnsureVisibleAndSelect` — DFS-walks the provider tree, expands ancestors on the path, rebuilds, then selects.
3. **`XdmAttribute.Namespace` is `NamespaceId`, not `string`.** The runtime `XmlSchemaValidator` API takes string namespace URIs. Added `XmlDocument.ResolveNamespace(NamespaceId)` so the walker can convert.
4. **xmlns attributes shouldn't be in the schema-validated attribute set.** XdmElement exposes namespace declarations via a separate `NamespaceDeclarations` (`NamespaceBinding`) list rather than through `Attributes`, so no filtering was needed — pleasant surprise from the model.
5. **Test text-content diagnostics attach to the parent element, not the text node.** ValidateText fires errors when text doesn't match the element's typed schema (e.g., `xs:gYear` rejecting "not-a-year"). Setting `ctx.CurrentNode = element` *before* the trailing ValidateText flush makes the inline glyph render on the `<year>` element row, not on a child text-node row (which doesn't render in NodeListView's tree).
6. **FluentAssertions 8.x went commercial under Xceed.** Pinned to 7.0.0 (last MIT-licensed) in phoenixmldb-core during Phase A test setup.
7. **`PhoenixmlDb.TestUtilities` middleman was breaking transitive package flow.** `IsPackable=false` suppresses default PackageReference flow to consumers. Dropped the project; tests reference packages directly.

## Architectural validations

- **Push-based validator beats serialize-and-reparse on every axis.** No round-trip cost. No line/col math. Exact node attachment by construction. The runtime `System.Xml.Schema.XmlSchemaValidator` was specifically designed for this push pattern — Microsoft documents it as the high-performance path. Should have used it from Plan 12.
- **Cross-repo plan execution worked.** Phase A in `phoenixmldb-core` shipped to NuGet (1.1.0); Phase B in `phoenixml-platform` consumed via PackageReference. Manual NuGet push gate (Plan 13's Task A5) caught the version-from-commit-count CI override that would have clobbered 1.1.0 — `release.sh --core-only --publish` was the right tool, not the CI workflow.
- **`SourceLine` / `SourceColumn` as additive init properties on `XdmNode` rippled cleanly.** XQuery, XSLT, and the database engine compile unchanged against Core 1.1.0. The DB engine pre-flight check (user's separate agent) confirmed no surprise interference patterns.
- **`DiagnosticIndex` + `IDiagnosticIndex` interface keeps the foundation control vocabulary-agnostic.** `XmlTreeNode` consults its provider's `DiagnosticIndex` (which is the editor-side concrete) via the platform-side abstraction. Plan 14's Workbench (XSLT/XQuery editors) builds its own DiagnosticIndex without touching `Phoenixml.Platform`.

## Smoke-test results (2026-05-09)

Loaded `library.xml` (two-book `<library>`, second book missing `@isbn` and with `<year>not-a-year</year>`).

| Action | Result |
|---|---|
| Open file with valid xsi: hint | ProblemsView populates with two errors after debounce. Glyphs on second `<book>` row and second `<year>` row. First book is clean. |
| Reported line/col | line 9 col 4 (second `<book>`), line 12 col 6 (second `<year>`) — match the original file exactly. |
| Double-click "missing isbn" | Editor selects the second `<book>` element. |
| Double-click "invalid gYear" | Editor selects the second `<year>` element. |
| Collapse the tree, then double-click an error | `EnsureVisibleAndSelect` expands the path; correct node is selected. |
| Edit `not-a-year` → `1992`, Enter | After ~500ms, gYear error clears, glyph on that row disappears. |
| Insert `@isbn` on second book + value | All errors clear; both glyphs disappear. |
| Validation runs on Document.Changed (debounced 500ms) | Synchronous on UI thread — no race with concurrent `_byId` mutation. |

## Rough-edges log (v0.8 backlog)

Ordered by friction.

### Resolved 2026-05-09 (Plan 13)

- [x] ~~**Click-to-navigate is regex-based and lands on the wrong node for duplicates.**~~ Push-based validator + DiagnosticLocation now select the exact offending node.
- [x] ~~**Inline error overlays unavailable.**~~ ✕ red / ⚠ yellow glyphs render on offending rows.
- [x] ~~**No source-position tracking on XdmNode.**~~ Core 1.1.0 ships `SourceLine` + `SourceColumn`.

### High friction

- [ ] **No way to view multiple documents simultaneously.** Tabs state machinery exists but no renderer; explorer covers most needs.
- [ ] **No "Set Schema…" UI** for documents without xsi: hints. User must edit `EditorSettings.PerDocumentSchema` JSON directly.
- [ ] **No file system watcher in the explorer.** External changes don't reflect.
- [ ] **Validator runs against in-memory tree but has no way to validate against the on-disk file directly.** For unmodified docs that's the same thing; for dirty docs it's correct (we want to validate what the user is editing). Acceptable.

### Medium friction

- [ ] **Per-document Save/Discard/Cancel choice in the quit dialog.** Currently bulk-only.
- [ ] **`MarkClean` on Discard All is a lie** — fine because the editor exits immediately, but reload-from-disk is the proper fix.
- [ ] **Save All silently skips never-saved docs.** Should force Save As for them.
- [ ] **Schema cache.** `LoadSchema` re-reads the XSD on every validation. Cache by path + timestamp.
- [ ] **No node-kind icons** (only diagnostic glyphs land in the IconKey channel right now).
- [ ] **No theme switching.** Dark only.
- [ ] **No Tab key navigation** between name/value cells during edit.
- [ ] **No collapse button for ProblemsView pane.** 120-dip fixed height.

### Low friction

- [ ] **Mutation helpers copy original positions to the replacement node.** After many edits, the source positions become misleading. Plan 14 might track a "is-synthesized-since-parse" flag.
- [ ] **Multi-namespace `xsi:schemaLocation`** — only the first ns/location pair is honored.
- [ ] **GridSplitter drag-to-zero** leaves explorer essentially invisible.
- [ ] **Recent Files entries with missing files are kept.**
- [ ] **Encoding declaration changes case** on save (`UTF-8` → `utf-8`).
- [ ] **No trailing newline** on saved files.
- [ ] **Mixed-content text rows show values in quotes.**
- [ ] **No app icon.**
- [ ] **No first-launch registration.**
- [ ] **No virtualization beyond `ListBox` default in NodeListView.**

## What's next: Plan 14 candidates

In priority order:

1. **"Set Schema…" File menu entry** — folder picker + write to `EditorSettings.PerDocumentSchema`.
2. **Schema cache** by (path, last-modified-utc).
3. **Reload-from-disk on Discard** (replace MarkClean lie).
4. **Per-document quit dialog** — checkboxes per row.
5. **FileSystemWatcher** in the explorer.
6. **Tab strip retry** with a custom layout panel.
7. **Real icon assets** for diag.error / diag.warning + node-kind icons.
8. **ProblemsView height splitter + collapse toggle.**

## Validated environment

- OS: Linux Mint 22.3 (Cinnamon, X11)
- .NET: 10.0
- Avalonia: 11.2.4
- Material.Icons.Avalonia: 2.x
- Phoenixml.Platform: 0.7.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.3.0 (in-repo)
- **PhoenixmlDb.Core: 1.1.0** (NuGet — published 2026-05-08)
