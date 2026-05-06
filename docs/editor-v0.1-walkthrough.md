# Phoenixml.Editor v0.1 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-06:** First runnable Editor that opens an XML file and shows
it in a tree-view + text-view dual-pane layout. Validated locally on Linux Mint 22.3
(Cinnamon / X11). Not published. Not packaged.

This document captures what works, what's intentionally minimal, and the rough-edges
list that seeds the v0.2 backlog.

## How to run it

```bash
cd /raid/elvogel/repos/phoenixml-platform
dotnet run --project src/Phoenixml.Editor --configuration Release
```

A window opens; click **File → Open** and pick an XML file. Recommended fixture:
`/raid/elvogel/repos/phoenixmldb/devtools/content/tutorials/04-xpath-essentials/input.xml`
(63-line `<catalog>` with categories, products, attributes, nested tags).

## What works in v0.1

- **Custom chrome** — single title bar with min/max/close buttons in the right place;
  no stacked OS chrome.
- **Tree view** — populated from the XDM via `XmlTreeNodeProvider`. Document /
  element / attribute / text / comment / processing-instruction nodes render with
  kind-appropriate labels (`<element>`, `@attribute`, etc.). Root + first level
  auto-expanded on Open so structure is visible immediately.
- **Text view** — multi-line, monospace, UTF-8-preserving. Shows the source file's
  raw text on Open (intentionally bypasses the round-trip serializer for v0.1; see
  rough-edges list below).
- **Status bar** — left-aligned segment shows the active document's filename, or
  `(no document)` when nothing is open.
- **File menu** — Open / Save / Save As / Exit. Save and Save As round-trip through
  `XmlDocument.SaveAsync` / `SaveAsAsync` (XDM serialization, not the in-memory text
  view).
- **Theming** — Phoenixml dark token set applied at app startup. `Background`,
  `Foreground`, and `BorderBrush` bindings across Shell + Editing controls resolve
  through the theme dictionary.
- **Title bar** — updates to `Phoenixml Editor — <filename>` when a document is
  active.

## Bugs caught and fixed during v0.1

These came up during the manual smoke test and were fixed before the first viewable
build. Each is worth remembering as a foundation invariant going forward.

1. **`ConfigureAwait(false)` in commands** caused `Call from invalid thread` crash —
   awaits in commands that mutate UI state must resume on the captured Avalonia
   `SynchronizationContext`. The standard CA2007 advice is library-code only;
   suppressed project-wide for the editor with a comment explaining why.
2. **Theme tokens never applied** — `App.OnFrameworkInitializationCompleted` now
   calls `FluentDarkTheme.Default.Apply(this)` before any window opens. Without it,
   every `{DynamicResource Phoenixml.Color.*}` lookup silently returned null.
3. **`Color` vs `IBrush` mismatch** — `Background="{DynamicResource ...}"` bindings
   need a brush, not a color value. Themes now store
   `ImmutableSolidColorBrush(Color.Parse(...))` under the same `Phoenixml.Color.*`
   keys.
4. **`Theme.Apply(IResourceHost)` over-narrowly cast to `StyledElement`** —
   `Application` isn't a `StyledElement` in Avalonia 11; both expose `Resources`
   through separate hierarchies. Fix accepts either.
5. **Status bar segment never updated** — segment was built once at startup and
   never replaced when `ActiveDocument` changed. Fixed with an `UpdateStatusBar`
   helper invoked from `OnActiveDocumentChanged`.
6. **Two title bars stacked** on Linux — `SystemDecorations="Full"` keeps OS
   chrome alongside our custom chrome. Changed to `SystemDecorations="BorderOnly"`
   plus `ExtendClientAreaChromeHints="NoChrome"`.
7. **Document tree label was the full `file://` URI** — caused horizontal overflow
   in the tree pane. `XmlTreeNode.DocumentLabel` now extracts just the filename.
8. **Right pane was single-line `utf-16`-declared XML on Open** — the underlying
   `XmlSerializer` produces unindented, encoding-mangled output. v0.1 sidesteps by
   reading the source file's raw bytes for display; the round-trip behavior is a
   real v0.2 work item.

## Rough-edges log (v0.2 backlog)

Things that are deliberately not addressed in v0.1, ordered by friction observed.

### High friction (felt while using it for the first time)

- [ ] **`XmlSerializer` produces single-line, `utf-16`-declared XML.** Currently
  hidden by the editor displaying the source file's raw text instead of the
  serializer output. As soon as edits land, this becomes painful — Save would
  produce mangled XML. Needs proper `XmlWriterSettings` (Indent=true, OmitXmlDeclaration
  optional, Encoding=UTF-8) wired into `PhoenixmlDb.Core`'s serializer or worked
  around in Editor.Xml.
- [ ] **Tree expand chevrons require precise clicking.** Avalonia default UX. v0.2
  should either (a) make the whole row toggle expansion on click, or (b) auto-expand
  more aggressively (current: 2 levels), or (c) increase chevron hit-target.
- [ ] **Splitter is technically visible but very subtle.** The 6-dip border-tone
  bar is easy to miss on dark theme. Consider a `Phoenixml.Color.Border` accent
  on hover, or a slightly brighter resting-state color, or a 1-dip light-tone
  highlight in the middle of the splitter for visual cue.
- [ ] **Edits in the text view don't sync back to the tree.** TextBox accepts
  typing but no command flows the change into XDM, so the tree stays stale.
  `IUndoStack` is plumbed through `Document` but no XML edit commands exist yet.
  Real editing is the v0.2 headline feature.

### Medium friction

- [ ] **No keyboard shortcuts.** Ctrl+O, Ctrl+S, Ctrl+W, Ctrl+Q would all be
  expected. Mnemonics in the menu work (`File` → Alt+F → `Open` → O) but no
  single-keystroke shortcuts are wired.
- [ ] **Single document only.** `EditorState.ActiveDocument` is a single nullable
  field; opening a second file replaces the first without warning. Tab strip is
  v0.2.
- [ ] **No recent files menu.** `SettingsService` from Hosting is not wired into
  Editor at all.
- [ ] **No problems pane.** `XmlSchemaValidator` and `ProblemsView` exist; nothing
  wires them together. Schema-validate-on-open + click-to-navigate would be a
  meaningful v0.2 feature.
- [ ] **No node-kind icons in the tree.** `XmlTreeNode.IconKey` returns
  `"icon.xml.element"` etc. but Editing's `TreeEditorView` template doesn't render
  icons. v0.2 will surface them once icon assets exist.
- [ ] **Status bar is fairly empty on the right.** The composable segment system
  exists; only the document filename is currently registered. Encoding, line/col,
  schema-binding, XPath of selected node are all designed (per spec §4.7) but
  unimplemented.
- [ ] **No theme switching.** App ships in dark; `FluentLightTheme` and
  `HighContrastTheme` exist but there's no toggle.

### Low friction (notice eventually)

- [ ] **No visible window-resize affordance** beyond the OS-level grab edges.
  Custom chrome eliminated the OS title bar; the OS still provides edge-grabbing
  but the visual hint isn't there.
- [ ] **No app icon** — taskbar/dock shows the default Avalonia icon.
- [ ] **No first-launch registration screen** — gated on phoenixml.net licensing
  backend per the licensing-API spec.
- [ ] **No error reporting if Open fails on a malformed file.** Currently throws
  silently and the document state stays the previous one (or null).
- [ ] **TextEditorView is a `TextBox`** — no syntax coloring, no line numbers, no
  intellisense. AvaloniaEdit integration paired with XML lexer is a v0.2+ work
  item.
- [ ] **No virtualization in the tree** — 100k-node documents will be slow.
  Acceptable for v0.1; required for serious documents in v0.2.
- [ ] **Tree's namespace handling is via `LocalName` only.** Namespaced documents
  show element labels without prefix/URI context.

## Architectural validations

The v0.1 build proves the following architectural choices were correct:

- **Vocabulary-agnostic Editing primitives.** `TreeEditorView`, `TextEditorView`,
  `SplitView` are reused without modification; `Phoenixml.Platform.Editor.Xml`
  supplies the providers. The seam from design spec §1.6 holds — Workbench will
  inherit a working tree by writing XSLT/XQuery providers.
- **Single mutation channel via Document.** Edit commands on `IUndoStack` will
  flow into the same `Changed` event the views are already subscribed to. v0.2's
  edit work doesn't require restructuring.
- **Theme as IBrush dictionary.** Once `Apply` populated brushes correctly, all
  controls picked up the right colors via `DynamicResource`. Per-product accent
  layering (per spec §4.4) will be additive.
- **Custom window chrome is feasible cross-platform.** Linux/Cinnamon/X11
  validated visually; Windows + macOS still pending interactive validation.

## Validated environment

- OS: Linux Mint 22.3 (Cinnamon, X11)
- .NET: 10.0 (preview)
- Avalonia: 11.2.4
- Phoenixml.Platform: 0.3.0 (consumed via ProjectReference; will swap to NuGet
  PackageReference once the editor is published independently)
- Phoenixml.Platform.Editor.Xml: 0.1.0 (same)
