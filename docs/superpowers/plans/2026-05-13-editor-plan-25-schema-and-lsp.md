# Editor v0.15 — Plan 25 Schema + LSP Pack — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Scope warning:** This plan is materially larger than Plan 23/24. It touches three repos (`phoenixmldb-xquery`, `phoenixmldb-xslt`, `phoenixml-platform`), introduces two new LSP server projects, an editor LSP client, an AvaloniaEdit-based text editor for non-XML files, and five schema-aware editor features. Expect 2–3 execution sessions. Tasks are ordered so partial progress ships cleanly: items 1–5 (Tasks 1–5) are independent schema work; items 6–9 (Tasks 6–9) form the LSP track; Task 10 is the walkthrough + commit.

**Goal:** Ship all 6 Theme-B carried items in one plan. Five schema-aware editor improvements (autocomplete by schema, multi-namespace, `xs:annotation` popups, Apply Schema right-click, type-aware Inserts) **plus** brand-new XQuery + XSLT LSP servers, an LSP client in the editor, and a text-buffer vocabulary for `.xq` / `.xsl` files (so the editor can host them).

**Architecture:**
- **Schema features (Tasks 1–5)** layer on top of the existing `SchemaResolver` / `SchemaCache` / `SchemaInsertOptionsService` infrastructure. No structural changes.
- **Text vocabulary (Task 6)** introduces a new `TextDocument` model alongside the existing `XmlDocument`. A new `TextEditorView` based on `AvaloniaEdit` hosts plain-text editing. The platform's `IDocument` abstraction already exists; we add a parallel implementation.
- **LSP servers (Tasks 7, 8)** are new .NET projects (`PhoenixmlDb.XQuery.LanguageServer`, `PhoenixmlDb.Xslt.LanguageServer`). Each wraps the corresponding engine. We use **StreamJsonRpc** for JSON-RPC framing — the de-facto .NET LSP transport. MVP capabilities: textDocument lifecycle (didOpen/didChange/didClose), publishDiagnostics, hover, completion. **No** semantic tokens, rename, definition, references, formatting, code actions, signature help, inlay hints — all Plan 26+.
- **LSP client (Task 9)** spawns each server as a child process per tab, talks over stdin/stdout, surfaces diagnostics into the editor's existing `DiagnosticIndex` + `ProblemsView`.

**Tech Stack:** Avalonia 11.2.4, AvaloniaEdit 11.x, StreamJsonRpc, PhoenixmlDb.Core/XQuery/Xslt (use the CPM pin you bumped earlier — Xslt now needs a real PackageReference in the LSP server csproj).

**Out of scope (Plan 26+):**
- Semantic tokens, rename, definition, references, formatting, code actions, signature help, inlay hints
- Workspace folders, project-level configuration sync
- LSP server hosting via TCP (only stdio in MVP)
- Syntax highlighting beyond AvaloniaEdit's default `XSL`/`XQuery` mode (we'll use generic; AvaloniaEdit has built-in support that may suit)
- Compare-view depth (carried from Plan 24)

---

## File structure (top-level overview)

### `phoenixmldb-xquery` (NEW server)

```
phoenixmldb-xquery/
└── src/
    └── PhoenixmlDb.XQuery.LanguageServer/
        ├── PhoenixmlDb.XQuery.LanguageServer.csproj
        ├── Program.cs                         (stdio entry point)
        ├── XQueryLanguageServer.cs            (JSON-RPC target)
        ├── DocumentBuffer.cs                  (per-URI source + version)
        ├── DiagnosticConverter.cs             (XQueryException → Lsp.Diagnostic)
        └── Lsp/
            ├── LspMessages.cs                 (Position, Range, Diagnostic, etc.)
            ├── InitializeParams.cs
            ├── TextDocumentSyncParams.cs
            ├── PublishDiagnosticsParams.cs
            ├── HoverParams.cs
            └── CompletionParams.cs
```

### `phoenixmldb-xslt` (NEW server)

```
phoenixmldb-xslt/
└── src/
    └── PhoenixmlDb.Xslt.LanguageServer/
        └── (mirror of XQuery server)
```

### `phoenixml-platform`

```
phoenixml-platform/
├── src/
│   ├── Phoenixml.Platform/
│   │   └── Documents/
│   │       └── TextDocument.cs                (NEW — plain-text IDocument)
│   ├── Phoenixml.Platform.Editor.Text/        (NEW project — text vocabulary)
│   │   ├── Phoenixml.Platform.Editor.Text.csproj
│   │   ├── TextDocumentProvider.cs            (IDocumentProvider for .xq/.xsl)
│   │   └── TextDocumentExtensions.cs
│   ├── Phoenixml.Editor/
│   │   ├── Views/
│   │   │   └── TextEditorView.axaml(.cs)      (NEW — AvaloniaEdit host)
│   │   ├── Lsp/
│   │   │   ├── LspClient.cs                   (NEW — JSON-RPC client)
│   │   │   ├── LspServerProcess.cs            (NEW — spawn + lifecycle)
│   │   │   ├── LspSession.cs                  (NEW — per-tab session)
│   │   │   └── LspDiagnosticSink.cs           (NEW — bridge to DiagnosticIndex)
│   │   └── EditorMainWindow.cs                (modify — route by file type)
│   └── Phoenixml.Platform.Editor.Xml/
│       ├── Validation/
│       │   ├── SchemaAnnotationLookup.cs      (NEW — xs:annotation text by node)
│       │   ├── MultiNamespaceSchemaResolver.cs (NEW — xsi:schemaLocation pairs)
│       │   └── SchemaDefaultValueResolver.cs  (NEW — xs:simpleType → default)
│       └── (existing files modified for Tasks 1, 4)
└── docs/
    └── editor-v0.15-walkthrough.md            (NEW)
```

---

## Tasks

### Task 1: Schema-aware autocompletion in the XPath bar

**Approach:** When the active document has a resolved schema, harvest valid element names at each ancestor's content model and use that list (instead of "all distinct names in document") as the completion source after `/` or `//`. Falls back to existing harvest when no schema is bound.

**Files:**
- Create: `src/Phoenixml.Platform.Editor.Xml/Validation/SchemaElementNameHarvester.cs`
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs` (extend `NamesRequested` callback)
- Create: `test/Phoenixml.Platform.Editor.Xml.Tests/Validation/SchemaElementNameHarvesterTests.cs`

- [ ] **Step 1: Write harvester tests**

```csharp
// 4 tests:
// - Harvests root-level globals from a simple XSD
// - Returns child-content-model elements for a known parent
// - Returns empty when XSD has no matching parent
// - Returns sorted-distinct list
```

- [ ] **Step 2: Implement `SchemaElementNameHarvester`**

```csharp
public static class SchemaElementNameHarvester
{
    public static IReadOnlyList<string> AllReachableElementNames(XmlSchemaSet schemas) { ... }
    public static IReadOnlyList<string> AllAttributeNames(XmlSchemaSet schemas) { ... }
}
```

Walks `schemas.GlobalElements`, `schemas.GlobalAttributes`, plus recursive content models. Sorts + dedupes.

- [ ] **Step 3: Wire schema source into `NamesRequested`**

In `EditorMainWindow.cs`, the existing `_xpathBar.NamesRequested` handler currently calls `XPathContextProvider.Collect*Names`. Extend:

```csharp
if (TryResolveSchema(doc) is { Schemas: var schemas })
{
    return kind switch
    {
        XPathContextKind.ElementName => SchemaElementNameHarvester.AllReachableElementNames(schemas),
        XPathContextKind.AttributeName => SchemaElementNameHarvester.AllAttributeNames(schemas),
        _ => XPathFunctionList.Match(prefix),
    };
}
// fall back to doc-harvested names
```

- [ ] **Step 4: Run tests + smoke**

---

### Task 2: Multi-namespace schema support

**Approach:** Extend `SchemaResolver` to parse `xsi:schemaLocation` (pairs of `<namespace> <uri>`) — today it only handles `xsi:noNamespaceSchemaLocation`. Load all referenced XSDs into a single `XmlSchemaSet`. Verify validation surfaces the right per-namespace errors.

**Files:**
- Modify: `src/Phoenixml.Editor/Validation/SchemaResolver.cs`
- Create: `test/Phoenixml.Editor.Tests/Validation/MultiNamespaceSchemaTests.cs`

- [ ] **Step 1: Write 3 tests**

```csharp
// - Two-namespace doc with both XSDs resolves successfully
// - Validator surfaces errors from the secondary namespace
// - Mixed valid/invalid: errors only on invalid side
```

- [ ] **Step 2: Parse `xsi:schemaLocation` token pairs**

Existing `XsiHint` resolution handles only one URI. Replace the parser with:

```csharp
private static IReadOnlyList<(string Namespace, string Path)> ParseSchemaLocation(string raw)
{
    // whitespace-separated tokens, paired (ns, uri).
    var tokens = raw.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    var pairs = new List<(string, string)>();
    for (int i = 0; i + 1 < tokens.Length; i += 2)
        pairs.Add((tokens[i], tokens[i + 1]));
    return pairs;
}
```

- [ ] **Step 3: Load each XSD into a shared `XmlSchemaSet`**

```csharp
var schemas = new XmlSchemaSet();
foreach (var (ns, path) in pairs)
{
    var absPath = Path.IsPathRooted(path) ? path : Path.Combine(docDir, path);
    schemas.Add(ns, absPath);
}
schemas.Compile();
```

- [ ] **Step 4: Run tests**

---

### Task 3: `xs:annotation` documentation popups

**Approach:** When hovering over a tree row, show a tooltip with the `xs:documentation` text from the matching XSD element/attribute declaration.

**Files:**
- Create: `src/Phoenixml.Platform.Editor.Xml/Validation/SchemaAnnotationLookup.cs`
- Modify: `src/Phoenixml.Platform/Editing/NodeListView.axaml` (add ToolTip binding to row)
- Modify: `src/Phoenixml.Platform/Editing/NodeListView.axaml.cs` / `NodeRowViewModel` (compute tooltip text on demand)
- Modify: `src/Phoenixml.Platform.Editor.Xml/Tree/XmlTreeNode.cs` (expose `Documentation` property from provider)
- Modify: `src/Phoenixml.Platform.Editor.Xml/Tree/XmlTreeNodeProvider.cs` (add `SchemaSet` setter for annotation source)
- Create: `test/Phoenixml.Platform.Editor.Xml.Tests/Validation/SchemaAnnotationLookupTests.cs`

- [ ] **Step 1: Write 3 lookup tests**

```csharp
// - Returns documentation for a known annotated element
// - Returns null when element has no annotation
// - Strips whitespace + concatenates multi-line documentation
```

- [ ] **Step 2: Implement `SchemaAnnotationLookup`**

```csharp
public static string? GetDocumentation(XmlSchemaElement element)
{
    var anno = element.Annotation;
    if (anno is null) return null;
    var sb = new StringBuilder();
    foreach (var item in anno.Items)
    {
        if (item is XmlSchemaDocumentation doc)
        {
            foreach (XmlNode m in doc.Markup ?? Array.Empty<XmlNode>())
                sb.Append(m.InnerText.Trim()).Append(' ');
        }
    }
    var result = sb.ToString().Trim();
    return result.Length == 0 ? null : result;
}
// Same shape for XmlSchemaAttribute.
```

- [ ] **Step 3: Plumb through the tree node**

`XmlTreeNodeProvider` gets a `XmlSchemaSet? SchemaSet { get; set; }` (or reuse a getter that looks at the resolution cache). `XmlTreeNode.Documentation { get; }` resolves the node against the schema set via `SchemaElementResolver` + `SchemaAnnotationLookup`.

- [ ] **Step 4: Add tooltip binding in AXAML**

In the row Border in `NodeListView.axaml`:

```xml
<Border ... ToolTip.Tip="{Binding Documentation}" ToolTip.ShowDelay="600">
```

`NodeRowViewModel.Documentation => Node.Documentation` (add a new property to `ITreeNode` with default `null`).

- [ ] **Step 5: Run tests + smoke**

---

### Task 4: Right-click "Apply Schema to..." in Explorer

**Approach:** Extend the existing Explorer context menu (per memory note: `/raid/elvogel/repos/phoenixml-platform/src/Phoenixml.Editor/Explorer/EditorExplorer.cs` already has the ContextMenu pattern) so right-clicking an `.xml` file shows "Apply Schema..." which opens a file picker for an XSD and persists the binding via `EditorSettings.PerDocumentSchema`.

**Files:**
- Modify: `src/Phoenixml.Editor/Explorer/EditorExplorer.cs`
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs` (handler that wires the picker + persistence)

- [ ] **Step 1: Add the menu item**

In the existing `ContextRequested` handler for `.xml` rows, before the existing `Open` / `Set as default for new docs` items, add:

```csharp
var applySchema = new MenuItem { Header = "_Apply Schema..." };
applySchema.Click += async (_, _) => await window.ApplySchemaToPathAsync(filePath);
menu.Items.Add(applySchema);
```

- [ ] **Step 2: Implement `ApplySchemaToPathAsync`**

```csharp
internal async Task ApplySchemaToPathAsync(string xmlPath)
{
    var topLevel = TopLevel.GetTopLevel(_window);
    if (topLevel is null) return;
    var files = await topLevel.StorageProvider.OpenFilePickerAsync(...);
    if (files.Count == 0) return;
    Settings.SetPerDocumentSchema(xmlPath, files[0].Path.LocalPath);
    NotifyAction($"Schema applied to {Path.GetFileName(xmlPath)}");
    // Re-validate any open tab pointing at xmlPath:
    if (_state.Tabs.FirstOrDefault(t => string.Equals(t.Document.Source?.LocalPath, xmlPath, StringComparison.Ordinal)) is { } tab)
        _validation.SetActiveDocument(tab.Document as XmlDocument);
}
```

- [ ] **Step 3: Smoke**

No unit tests — UI-only wiring.

---

### Task 5: Type-aware default values for schema-aware Insert

**Approach:** When the user picks an element from the schema-aware Insert submenu, prefill the new element's text content (or attribute value) from the schema type. Map common simple types: `xs:string` → `""`, `xs:integer` → `"0"`, `xs:boolean` → `"false"`, `xs:date` → `today()`, `xs:dateTime` → ISO now, `xs:decimal` → `"0.0"`. Enumerations → first enumeration value. Falls back to empty string.

**Files:**
- Create: `src/Phoenixml.Platform.Editor.Xml/Validation/SchemaDefaultValueResolver.cs`
- Modify: `src/Phoenixml.Platform.Editor.Xml/Editing/InsertElementCommand.cs` (or wherever schema-aware Insert is executed) — accept optional default text content
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs` (wire the resolver into the Insert command)
- Create: `test/Phoenixml.Platform.Editor.Xml.Tests/Validation/SchemaDefaultValueResolverTests.cs`

- [ ] **Step 1: Write 6 resolver tests**

```csharp
// - xs:string returns ""
// - xs:integer returns "0"
// - xs:boolean returns "false"
// - xs:date returns ISO today
// - xs:enumeration of {a,b,c} returns "a"
// - Unknown type returns ""
```

- [ ] **Step 2: Implement `SchemaDefaultValueResolver`**

```csharp
public static string GetDefault(XmlSchemaType? type)
{
    if (type is null) return string.Empty;
    if (type is XmlSchemaSimpleType st && st.Content is XmlSchemaSimpleTypeRestriction restr)
    {
        // enumeration?
        foreach (var facet in restr.Facets)
            if (facet is XmlSchemaEnumerationFacet enumFacet)
                return enumFacet.Value ?? string.Empty;
        // fall through to base name
    }
    return type.QualifiedName.Name switch
    {
        "string" or "normalizedString" or "token" => "",
        "integer" or "int" or "long" or "short" or "byte"
            or "nonNegativeInteger" or "positiveInteger" => "0",
        "decimal" or "double" or "float" => "0.0",
        "boolean" => "false",
        "date" => DateTime.UtcNow.ToString("yyyy-MM-dd"),
        "dateTime" => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        "time" => DateTime.UtcNow.ToString("HH:mm:ss"),
        _ => "",
    };
}
```

- [ ] **Step 3: Wire into the schema-aware Insert path**

The existing `NodeInsertChildElementWithNameRequested` handler currently calls `InsertElementCommand(doc, parent, name)`. After insert, look up the inserted element's schema type via `SchemaElementResolver`, compute its default text, and if non-empty append a text child via a new `SetTextContentCommand` wrapped in a `CompoundCommand` so the whole thing is one undo step.

- [ ] **Step 4: Run tests + smoke**

---

### Task 6: Text-buffer document model + `TextEditorView`

**Approach:** Add `TextDocument` (plain-text `IDocument` impl) and `TextEditorView` (AvaloniaEdit host). `EditorMainWindow` routes opens by file extension: `.xml` / `.xsd` → existing tree view; `.xq` / `.xqy` / `.xsl` / `.xslt` → text view. Both share the existing tab strip + dirty tracking.

**Files:**
- Add: `Avalonia.AvaloniaEdit` to `Directory.Packages.props`
- Modify: `src/Phoenixml.Editor/Phoenixml.Editor.csproj` (`<PackageReference Include="Avalonia.AvaloniaEdit" />`)
- Create: `src/Phoenixml.Platform.Editor.Text/Phoenixml.Platform.Editor.Text.csproj`
- Create: `src/Phoenixml.Platform.Editor.Text/TextDocument.cs`
- Create: `src/Phoenixml.Platform.Editor.Text/TextDocumentProvider.cs`
- Create: `src/Phoenixml.Editor/Views/TextEditorView.axaml(.cs)`
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs` (extension-routing in Open + tab building)
- Create: `test/Phoenixml.Platform.Editor.Text.Tests/Phoenixml.Platform.Editor.Text.Tests.csproj`
- Create: `test/Phoenixml.Platform.Editor.Text.Tests/TextDocumentTests.cs`

- [ ] **Step 1: Pin AvaloniaEdit in CPM**

```xml
<!-- Directory.Packages.props -->
<PackageVersion Include="Avalonia.AvaloniaEdit" Version="11.3.0" />
```

- [ ] **Step 2: Create the text-vocabulary project**

`Phoenixml.Platform.Editor.Text.csproj` references `Phoenixml.Platform.Abstractions` only — keep it independent of XML vocabulary.

- [ ] **Step 3: Implement `TextDocument`**

```csharp
public sealed class TextDocument : Document
{
    private string _text;
    private long _version;

    public TextDocument(PlatformDocumentId id, Uri? source, string displayName, string text)
        : base(id, source, displayName, DocumentKind.Text)
    {
        _text = text;
        _version = 1;
    }

    public string Text => _text;
    public long Version => _version;

    public void Replace(int offset, int length, string newText)
    {
        _text = _text.Substring(0, offset) + newText + _text.Substring(offset + length);
        _version++;
        MarkDirty();
        RaiseChanged(new TextEdit(offset, length, newText));
    }

    public override async ValueTask SaveAsync(CancellationToken ct) { ... File.WriteAllTextAsync ... }
}

public sealed record TextEdit(int Offset, int Length, string NewText) : DocumentChange;
```

Note: `DocumentKind` enum needs a new `Text` variant — modify the abstraction.

- [ ] **Step 4: Write 4 `TextDocumentTests`**

- Load from string, read Text
- Replace within text bounds
- Version increments on replace
- IsDirty after replace, clean after save

- [ ] **Step 5: Implement `TextEditorView`**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:ae="https://github.com/avaloniaui/avaloniaedit"
             x:Class="Phoenixml.Editor.Views.TextEditorView">
    <ae:TextEditor Name="Editor"
                   FontFamily="Cascadia Mono, Consolas, monospace"
                   ShowLineNumbers="True" />
</UserControl>
```

Code-behind: `SetDocument(TextDocument doc)` initializes `Editor.Document = new TextEditorDocument(doc.Text)` and wires `Editor.Document.Changing` → `doc.Replace(...)`. Likewise listen to `doc.Changed` to update the buffer when external (LSP-driven) edits arrive — initial MVP can skip the round-trip since edits go from view → doc → LSP.

- [ ] **Step 6: Route by file extension in `EditorMainWindow`**

In the existing Open handler:

```csharp
var ext = Path.GetExtension(path).ToLowerInvariant();
IDocument doc = ext switch
{
    ".xq" or ".xqy" or ".xquery" or ".xsl" or ".xslt" =>
        await TextDocument.LoadAsync(path, ct),
    _ => await XmlDocument.LoadAsync(path, ct),
};
```

Tab construction picks `TextEditorView` for text docs, `NodeListView` for XML docs.

- [ ] **Step 7: Run tests + smoke**

Open an `.xsl` file; verify text editor opens with content. Type a character; tab title shows the dirty marker.

---

### Task 7: `PhoenixmlDb.XQuery.LanguageServer`

**Approach:** New project in `phoenixmldb-xquery/src/`. Reads JSON-RPC over stdin/stdout via StreamJsonRpc. Implements minimal LSP capabilities: textDocument lifecycle, publishDiagnostics, hover, completion.

**Files (in `phoenixmldb-xquery`):**
- Create: `src/PhoenixmlDb.XQuery.LanguageServer/PhoenixmlDb.XQuery.LanguageServer.csproj`
- Create: `src/PhoenixmlDb.XQuery.LanguageServer/Program.cs`
- Create: `src/PhoenixmlDb.XQuery.LanguageServer/XQueryLanguageServer.cs`
- Create: `src/PhoenixmlDb.XQuery.LanguageServer/DocumentBuffer.cs`
- Create: `src/PhoenixmlDb.XQuery.LanguageServer/DiagnosticConverter.cs`
- Create: `src/PhoenixmlDb.XQuery.LanguageServer/Lsp/*.cs` (message DTOs)
- Create: `tests/PhoenixmlDb.XQuery.LanguageServer.Tests/...`

- [ ] **Step 1: Pin StreamJsonRpc**

```xml
<PackageVersion Include="StreamJsonRpc" Version="2.18.0" />
```

- [ ] **Step 2: csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>PhoenixmlDb.XQuery.LanguageServer</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../PhoenixmlDb.XQuery/PhoenixmlDb.XQuery.csproj" />
    <PackageReference Include="StreamJsonRpc" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: LSP message DTOs**

`Lsp/LspMessages.cs`:

```csharp
public sealed record Position(int Line, int Character);
public sealed record Range(Position Start, Position End);
public sealed record Diagnostic(Range Range, int? Severity, string? Code, string Message);
public sealed record TextDocumentIdentifier(string Uri);
public sealed record VersionedTextDocumentIdentifier(string Uri, int Version);
public sealed record TextDocumentItem(string Uri, string LanguageId, int Version, string Text);
public sealed record TextDocumentContentChangeEvent(Range? Range, string Text);
// ... etc.
```

- [ ] **Step 4: Implement `DocumentBuffer`**

Per-URI source-of-truth. Maintains `Text` and `Version`. Supports applying incremental change events. Computes byte/char offsets from `Position(line, character)`.

- [ ] **Step 5: Implement `DiagnosticConverter`**

```csharp
public static IReadOnlyList<Diagnostic> Convert(XQueryException ex, DocumentBuffer buf)
{
    var list = new List<Diagnostic>();
    if (ex.PrimaryLocation is SourceLocation pri)
        list.Add(new Diagnostic(
            new Range(buf.OffsetToPosition(pri.StartIndex), buf.OffsetToPosition(pri.EndIndex + 1)),
            Severity: 1, Code: ex.ErrorCode, Message: ex.Message));
    foreach (var rel in ex.RelatedLocations)
        list.Add(new Diagnostic(...));
    return list;
}
```

(`PrimaryLocation` / `RelatedLocations` are the surfaces added in XQuery 1.3.6 — verify these exact names.)

- [ ] **Step 6: Implement `XQueryLanguageServer` JSON-RPC handlers**

```csharp
public sealed class XQueryLanguageServer
{
    [JsonRpcMethod("initialize")]
    public InitializeResult Initialize(InitializeParams p) => new(
        new ServerCapabilities {
            TextDocumentSync = 2,  // incremental
            HoverProvider = true,
            CompletionProvider = new() { TriggerCharacters = new[] { ":", "$", "/" } },
        });

    [JsonRpcMethod("textDocument/didOpen")]
    public void DidOpen(DidOpenTextDocumentParams p) { ... store + diag }

    [JsonRpcMethod("textDocument/didChange")]
    public void DidChange(DidChangeTextDocumentParams p) { ... apply + diag }

    [JsonRpcMethod("textDocument/didClose")]
    public void DidClose(DidCloseTextDocumentParams p) { ... remove }

    [JsonRpcMethod("textDocument/hover")]
    public Hover? Hover(HoverParams p) { ... }

    [JsonRpcMethod("textDocument/completion")]
    public CompletionList? Completion(CompletionParams p) { ... }
}
```

After each didOpen/didChange the server compiles the buffer, captures any `XQueryException`, converts to diagnostics, and sends `textDocument/publishDiagnostics` notification back to the client.

- [ ] **Step 7: `Program.cs` — stdio bootstrap**

```csharp
var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();
var jsonRpc = JsonRpc.Attach(stdout, stdin, new XQueryLanguageServer());
await jsonRpc.Completion;
```

- [ ] **Step 8: Write 3 server integration tests**

- Round-trip initialize + capabilities
- didOpen with a valid query produces zero diagnostics
- didOpen with `1 +` produces a parse-error diagnostic at the right range

---

### Task 8: `PhoenixmlDb.Xslt.LanguageServer`

Mirror of Task 7 against `PhoenixmlDb.Xslt`. Same shape, same MVP capabilities.

**Files (in `phoenixmldb-xslt`):**
- Same layout as Task 7 but rooted under `phoenixmldb-xslt/src/PhoenixmlDb.Xslt.LanguageServer/`

- [ ] **Step 1: csproj + StreamJsonRpc pin**

- [ ] **Step 2: Reuse LSP message DTOs (copy-paste from XQuery server)**

We don't share a common LSP types library yet — that's Plan 26 polish.

- [ ] **Step 3: XsltLanguageServer with the same five handlers**

`Hover` and `Completion` for XSLT can return empty results in MVP — the diagnostics path is the primary value.

- [ ] **Step 4: 3 integration tests**

---

### Task 9: Editor LSP client + diagnostic surfacing

**Files:**
- Create: `src/Phoenixml.Editor/Lsp/LspServerProcess.cs`
- Create: `src/Phoenixml.Editor/Lsp/LspClient.cs`
- Create: `src/Phoenixml.Editor/Lsp/LspSession.cs`
- Create: `src/Phoenixml.Editor/Lsp/LspDiagnosticSink.cs`
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs` (spawn session when a `.xq`/`.xsl` tab opens, dispose on close)
- Create: `test/Phoenixml.Editor.Tests/Lsp/LspClientTests.cs`

- [ ] **Step 1: `LspServerProcess` — spawn + dispose**

```csharp
public sealed class LspServerProcess : IDisposable
{
    public LspServerProcess(string exePath)
    {
        _proc = Process.Start(new ProcessStartInfo {
            FileName = exePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        });
    }
    public Stream Stdin => _proc.StandardInput.BaseStream;
    public Stream Stdout => _proc.StandardOutput.BaseStream;
    public void Dispose() { try { _proc.Kill(); } catch { } _proc.Dispose(); }
}
```

- [ ] **Step 2: `LspClient` — JSON-RPC client**

```csharp
public sealed class LspClient : IAsyncDisposable
{
    private readonly LspServerProcess _proc;
    private readonly JsonRpc _rpc;

    public LspClient(LspServerProcess proc)
    {
        _proc = proc;
        _rpc = JsonRpc.Attach(_proc.Stdin, _proc.Stdout, this);
    }

    [JsonRpcMethod("textDocument/publishDiagnostics")]
    public void OnPublishDiagnostics(PublishDiagnosticsParams p) => DiagnosticsReceived?.Invoke(p);

    public event Action<PublishDiagnosticsParams>? DiagnosticsReceived;

    public Task<InitializeResult> InitializeAsync() => _rpc.InvokeAsync<InitializeResult>("initialize", new { });
    public Task NotifyDidOpenAsync(...) => _rpc.NotifyAsync("textDocument/didOpen", ...);
    public Task NotifyDidChangeAsync(...) => _rpc.NotifyAsync("textDocument/didChange", ...);
    public ValueTask DisposeAsync() { _rpc.Dispose(); _proc.Dispose(); return ValueTask.CompletedTask; }
}
```

- [ ] **Step 3: `LspSession` — per-tab session**

Couples a `TextDocument` with an `LspClient`. On `TextDocument.Changed`, sends `didChange` with incremental range. On `DiagnosticsReceived`, routes to `LspDiagnosticSink`.

- [ ] **Step 4: `LspDiagnosticSink` — bridge to editor `ProblemsView`**

Converts LSP `Diagnostic` → editor `Diagnostic` (already exists for XML), pushes into `DiagnosticIndex`, triggers ProblemsView refresh.

- [ ] **Step 5: Wire in `EditorMainWindow`**

```csharp
// When opening a .xq / .xsl tab, look up the server binary path (assume nearby
// `tools/` folder for MVP), spawn LspServerProcess, create LspClient + LspSession.
// Store one session per open tab; dispose on tab-close.
```

- [ ] **Step 6: 3 client integration tests**

Spawn a stub server in-process (using StreamJsonRpc on in-memory streams) and verify the client roundtrips initialize, didOpen, and receives publishDiagnostics correctly.

- [ ] **Step 7: Smoke (manual)**

Open an `.xq` file with `1 +` — squiggle appears under the trailing `+`; Problems panel shows the parse error.

---

### Task 10: Walkthrough + commit

**Files:**
- Create: `docs/editor-v0.15-walkthrough.md`
- Modify: `docs/editor-v0.14-walkthrough.md` (link forward to v0.15)

- [ ] **Step 1: Write walkthrough** (template mirrors v0.13 / v0.14, sections for each of the 9 implementation tasks, smoke table)

- [ ] **Step 2: Update CPM version pins if engines needed bumps for new error surfaces**

- [ ] **Step 3: Run full suite**

```bash
dotnet test --nologo
```

Expected: all green; +25 new tests (4 schema harvester, 3 multi-namespace, 3 annotation, 6 default value, 4 TextDocument, 3 XQuery LSP, 3 XSLT LSP, 3 LSP client).

- [ ] **Step 4: Commit per-repo**

Three separate commits since the work spans three repos:
- `phoenixmldb-xquery`: feat: XQuery LSP server (Plan 25 #6)
- `phoenixmldb-xslt`: feat: XSLT LSP server (Plan 25 #6)
- `phoenixml-platform`: feat: schema features + LSP client + text vocabulary (Plan 25 #1-5, #7-9)

---

## Out of scope (Plan 26+)

- Semantic tokens, signature help, inlay hints, codeAction, rename, definition, references, formatting
- Workspace folders / multi-root projects
- TCP-based LSP transport (only stdio in MVP)
- Syntax highlighting beyond AvaloniaEdit defaults
- Shared `PhoenixmlDb.Lsp.Common` library — both servers copy-paste the message DTOs in MVP
- Compare-view structural alignment, kbd nav, in-line edit (carried from Plan 24)
- LSP client process-lifecycle robustness (restart on crash, version mismatch handling)
