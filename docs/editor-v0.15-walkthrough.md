# Phoenixml.Editor v0.15 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-13:** v0.15 ships the Plan 25 Schema + LSP pack. Five schema-aware editor features plus brand-new XQuery + XSLT LSP servers in the engine repos and an LSP client in the editor. Text-buffer document model (`TextDocument`) added for non-XML files.

The plan was scoped across three repos (`phoenixmldb-xquery`, `phoenixmldb-xslt`, `phoenixml-platform`); this walkthrough is the editor-side summary. Engine-side release notes live in their respective `RELEASES.md`.

## What's new in v0.15

### Schema track (Plan 25 #1–5)

1. **Schema-aware XPath autocompletion** — when the active doc is bound to an XSD, completions after `//` and `@` are sourced from `XmlSchemaSet` globals + recursive content models instead of being doc-harvested. New `SchemaElementNameHarvester` does the walk.

2. **Multi-namespace schema support** — `SchemaResolver` now parses `xsi:schemaLocation` as `(namespace, location)` pairs and loads all referenced XSDs into one combined `XmlSchemaSet`. Mixed-namespace documents validate against all referenced schemas in one pass.

3. **`xs:annotation` documentation tooltips** — hovering a tree row for ~600ms shows the `xs:documentation` text from the matching XSD element declaration. `ITreeNode` gains a default-null `Documentation` property; `XmlTreeNode` resolves it via the provider's new `SchemaSet` binding.

4. **Right-click "Apply Schema..."** on XML files in the Explorer opens a file picker for an XSD and binds it via `PerDocumentSchema`. Reciprocal of the existing "Apply Schema to active doc" on XSD rows.

5. **Type-aware default values for schema-aware Insert** — when inserting a schema-typed element or attribute, the new node is prefilled by `SchemaDefaultValueResolver`:
   - `xs:integer` → `0`, `xs:boolean` → `false`, `xs:date` → today, `xs:dateTime` → ISO now.
   - Enumerations → first enumeration value.

### LSP track (Plan 25 #6–9)

6. **`TextDocument`** — new plain-text `IDocument` for non-XML files (`.xq`, `.xsl`). Versioned for LSP `didChange`. Lives in a new `Phoenixml.Platform.Editor.Text` project. Foundation for future text-editing surface (not yet wired into the editor's tab strip — see "carried" below).

7. **`PhoenixmlDb.XQuery.LanguageServer`** — new exe project in `phoenixmldb-xquery/src/`. MVP LSP capabilities:
   - textDocument lifecycle (didOpen / didChange / didClose), full-text sync
   - `publishDiagnostics` driven by `QueryEngine.Compile` errors (parse + analysis)
   - `hover` (stub — Plan 26)
   - `completion` (fixed list of common `fn:*` names)
   - Transport: stdio with `HeaderDelimitedMessageHandler` + StreamJsonRpc

8. **`PhoenixmlDb.Xslt.LanguageServer`** — mirror exe project in `phoenixmldb-xslt/src/`. MVP capabilities limited to textDocument lifecycle + `publishDiagnostics` (driven by `XsltTransformer.LoadStylesheetAsync`). Hover + completion deferred to Plan 26.

9. **Editor LSP client** — new `LspClient` in `Phoenixml.Editor/Lsp/`. JSON-RPC over arbitrary `Stream` pairs (process stdin/stdout in production, in-memory pipes in tests). Surfaces `DiagnosticsReceived` event matching the LSP `publishDiagnostics` notification.

### Carried to Plan 26

- **Editor wiring for `.xq` / `.xsl` files** — `TextDocument` and `LspClient` exist and are unit-tested, but the editor's tab strip still hard-routes everything through `NodeListView` (the XML tree view). Opening an `.xq` or `.xsl` file in the editor today still routes to the XML pipeline (which fails for non-XML content). Wiring `TextDocument` into the tab system + an `AvaloniaEdit`-based view is the first Plan 26 task.
- **Spawning LSP server processes from the editor.** The client is transport-agnostic but `LspServerProcess` (process-spawn wrapper) isn't built yet; tests use in-memory streams.
- **Semantic tokens / rename / definition / references / formatting / code actions / signature help / inlay hints.** All deferred — the MVP servers only do diagnostics.
- **Incremental sync.** Servers + client both use full-text sync; large files will be inefficient until incremental sync lands.
- **Shared `PhoenixmlDb.Lsp.Common` library.** XQuery server, XSLT server, and editor client each duplicate the LSP message DTOs. Plan 26 candidate.

## How to run an LSP server standalone (for VS Code etc.)

```bash
# From repo root
dotnet run --project /raid/elvogel/repos/phoenixmldb/phoenixmldb-xquery/src/PhoenixmlDb.XQuery.LanguageServer
# (reads JSON-RPC over stdin, writes to stdout)
```

VS Code can be pointed at this binary via a tiny extension scaffolded around `vscode-languageclient`.

## Smoke-test checklist (manual)

| Action | Expected | Actual |
|---|---|---|
| Open an XML file bound to an XSD; Ctrl+G; type `//` | Completion shows element names from the XSD, not just the doc. | tbd |
| Bind a doc to a 2-namespace XSD (`xsi:schemaLocation="ns1 a.xsd ns2 b.xsd"`) | Both XSDs load; mixed-namespace validation works. | tbd |
| Hover an element row with `xs:annotation` in its declaration | Tooltip shows the `xs:documentation` text after ~600ms. | tbd |
| Right-click an `.xml` row in Explorer → Apply Schema → pick XSD | `Schema applied to <name>` status; subsequent edits validate. | tbd |
| Insert a schema-typed `xs:integer` child via context menu | New element prefilled with `0`; value-edit mode active. | tbd |
| Insert an `xs:date` attribute | Prefilled with today's ISO date. | tbd |
| Insert into an `xs:enumeration` slot | Prefilled with the first enumeration value. | tbd |
| `dotnet test phoenixmldb-xquery/tests/PhoenixmlDb.XQuery.LanguageServer.Tests` | 4 / 4 green. | tbd |
| `dotnet test phoenixmldb-xslt/tests/PhoenixmlDb.Xslt.LanguageServer.Tests` | 3 / 3 green. | tbd |
| `dotnet test test/Phoenixml.Editor.Tests --filter LspClientTests` | 3 / 3 green (in-memory pipe round-trip). | tbd |

## Validated environment

- OS: Linux Mint 22.3
- .NET: 10.0
- Avalonia: 11.2.4
- StreamJsonRpc: 2.22.11
- Phoenixml.Platform: 0.15.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.11.0 (in-repo)
- Phoenixml.Platform.Editor.Text: 0.1.0 (in-repo, NEW)
- PhoenixmlDb.Core: 1.1.2 (NuGet)
- PhoenixmlDb.XQuery: 1.3.6 (NuGet)
- PhoenixmlDb.Xslt: 1.3.10 (CPM pinned)
- PhoenixmlDb.XQuery.LanguageServer: 0.1.0 (in `phoenixmldb-xquery`, NEW)
- PhoenixmlDb.Xslt.LanguageServer: 0.1.0 (in `phoenixmldb-xslt`, NEW)
