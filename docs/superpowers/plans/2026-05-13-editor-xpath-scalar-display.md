# Editor — XPath Scalar Result Display (Plan 22) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When an XPath expression returns atomic value(s) instead of nodes (e.g. `count(//book)`, `//book/title/string()`, `1 to 5`), render the serialized scalar in the status bar's `selected-xpath` segment so the user gets immediate feedback. Until Plan 22, scalar results were silently dropped.

**Architecture:** Extend `XPathQueryService.EvaluateAsync` to capture atomic items alongside nodes. When the result contains no nodes but at least one atomic, format the atomics (comma-joined, truncated to ~80 chars) into `XPathResult.Scalar`. The controller forwards the scalar to the editor host, which renders it in the existing `selected-xpath` status segment, taking precedence over the selected-node XPath while the bar is open. Closing the bar restores selected-node XPath.

**Tech Stack:** PhoenixmlDb.XQuery (atomic items as boxed CLR primitives), Avalonia status bar, existing `XPathBuilder` + `StatusBarSegment` plumbing.

---

## File structure

| File | Role |
|---|---|
| `src/Phoenixml.Platform.Editor.Xml/Find/XPathQueryService.cs` | Modify — add `Scalar` field to `XPathResult`, format atomics |
| `src/Phoenixml.Editor/Find/XPathQueryController.cs` | Modify — surface scalar via new host callback |
| `src/Phoenixml.Editor/Find/IXPathQueryHost` (in `XPathQueryController.cs`) | Modify — add `SetXPathScalar(string?)` |
| `src/Phoenixml.Editor/EditorMainWindow.cs` | Modify — `_xpathScalar` field, status-bar precedence, `IXPathQueryHost.SetXPathScalar` impl |
| `test/Phoenixml.Platform.Editor.Xml.Tests/Find/XPathQueryServiceTests.cs` | Modify — add scalar tests |
| `docs/editor-v0.12-walkthrough.md` | Modify — Plan 22 walkthrough section, move scalar from backlog to resolved |

---

## Tasks

### Task 1: Extend `XPathResult` with `Scalar` field

**Files:**
- Modify: `src/Phoenixml.Platform.Editor.Xml/Find/XPathQueryService.cs`

- [ ] **Step 1: Add `Scalar` to the record**

In `XPathQueryService.cs`, replace the `XPathResult` record declaration:

```csharp
/// <summary>
/// Result of an XPath evaluation: the matching <see cref="NodeId"/>s in document order,
/// a formatted scalar string when the expression yielded only atomics, or an error.
/// </summary>
/// <param name="Nodes">Matching nodes (empty when the result was scalar or errored).</param>
/// <param name="Scalar">
/// Comma-joined, length-capped serialization of atomic items when the expression yielded
/// no nodes (e.g. <c>count(//book)</c> → <c>"12"</c>; <c>1 to 5</c> → <c>"(1, 2, 3, 4, 5)"</c>).
/// <see langword="null"/> when the result contained nodes, was empty, or errored.
/// </param>
/// <param name="Error">Diagnostic message, or <see langword="null"/> on success.</param>
public sealed record XPathResult(
    IReadOnlyList<NodeId> Nodes,
    string? Scalar,
    string? Error);
```

- [ ] **Step 2: Verify call sites flag**

Run: `dotnet build src/Phoenixml.Editor/Phoenixml.Editor.csproj --nologo 2>&1 | grep -E "error|Error" | head -20`
Expected: errors at every `new XPathResult(...)` call site missing the new arg, plus errors in `XPathQueryController.cs` and `XPathQueryServiceTests.cs`. This drives the next steps.

---

### Task 2: Format atomics in the service

**Files:**
- Modify: `src/Phoenixml.Platform.Editor.Xml/Find/XPathQueryService.cs`

- [ ] **Step 1: Add the formatter helper**

After the `XmlDocumentNodeStore` nested class, before the closing `}` of `XPathQueryService`, add:

```csharp
/// <summary>
/// Formats a list of atomic XQuery result items into a single status-bar string.
/// One item → bare value (<c>"12"</c>); multiple → parenthesized comma list
/// (<c>"(1, 2, 3, 4, 5)"</c>); truncated at <see cref="MaxScalarLength"/>
/// with <c>"… +N more"</c> suffix.
/// </summary>
internal static string FormatAtomics(IReadOnlyList<object?> atomics)
{
    if (atomics.Count == 1)
        return FormatAtomic(atomics[0]);

    var sb = new System.Text.StringBuilder("(");
    var rendered = 0;
    for (int i = 0; i < atomics.Count; i++)
    {
        var s = FormatAtomic(atomics[i]);
        // +2 for ", " separator on subsequent items, +1 for closing ')'.
        var pending = sb.Length + s.Length + (rendered > 0 ? 2 : 0) + 1;
        if (pending > MaxScalarLength && rendered > 0)
        {
            sb.Append(", … +").Append(atomics.Count - rendered).Append(" more");
            break;
        }
        if (rendered > 0) sb.Append(", ");
        sb.Append(s);
        rendered++;
    }
    sb.Append(')');
    return sb.ToString();
}

private static string FormatAtomic(object? item)
{
    if (item is null) return "()";
    if (item is bool b) return b ? "true" : "false";
    // Invariant culture — XQuery atomics serialize without locale formatting.
    return System.Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
}

private const int MaxScalarLength = 80;
```

- [ ] **Step 2: Collect atomics and route them**

Replace the `await foreach` block and the final `return` in `EvaluateAsync` with:

```csharp
        var matches = new List<NodeId>();
        var atomics = new List<object?>();
        try
        {
            await foreach (var item in engine.ExecuteAsync(
                compile.ExecutionPlan,
                container: default,
                initialContextItem: doc.Root,
                cancellationToken).ConfigureAwait(false))
            {
                if (item is XdmNode node)
                    matches.Add(node.Id);
                else if (item is not null)
                    atomics.Add(item);
            }
        }
        catch (XQueryRuntimeException ex)
        {
            return new XPathResult(Array.Empty<NodeId>(), Scalar: null, Error: ex.Message);
        }

        // Node-set wins when present — tinting is more useful than text. Atomics surface
        // only when there are no nodes.
        var scalar = matches.Count == 0 && atomics.Count > 0
            ? FormatAtomics(atomics)
            : null;

        return new XPathResult(matches, scalar, Error: null);
```

- [ ] **Step 3: Update the early returns**

Replace the three remaining `return new XPathResult(Array.Empty<NodeId>(), ...)` sites in `EvaluateAsync` (whitespace check, parse exception, compile failure) with the 3-arg form:

```csharp
        if (string.IsNullOrWhiteSpace(xpath))
            return new XPathResult(Array.Empty<NodeId>(), Scalar: null, Error: null);
```

```csharp
        catch (XQueryParseException ex)
        {
            return new XPathResult(Array.Empty<NodeId>(), Scalar: null, Error: ex.Message);
        }
```

```csharp
        if (!compile.Success || compile.ExecutionPlan is null)
        {
            var msg = compile.Errors is { Count: > 0 }
                ? string.Join("; ", compile.Errors.Select(e => e.Message))
                : "XPath compilation failed.";
            return new XPathResult(Array.Empty<NodeId>(), Scalar: null, Error: msg);
        }
```

- [ ] **Step 4: Build the service**

Run: `dotnet build src/Phoenixml.Platform.Editor.Xml/Phoenixml.Platform.Editor.Xml.csproj --nologo 2>&1 | tail -10`
Expected: build succeeds; 0 errors. (Test + Editor projects still red — fixed next.)

---

### Task 3: Service-level unit tests for scalars

**Files:**
- Modify: `test/Phoenixml.Platform.Editor.Xml.Tests/Find/XPathQueryServiceTests.cs`

- [ ] **Step 1: Update the existing scalar-no-node test**

Replace the body of `EvaluateScalarResultProducesNoNodes` with:

```csharp
    [Fact]
    public async Task EvaluateScalarResultPopulatesScalarField()
    {
        // count() returns an atomic — must populate Scalar, leave Nodes empty.
        var doc = ParseDoc(LibraryXml);

        var r = await Eval(doc, "count(//book)");

        Assert.Null(r.Error);
        Assert.Empty(r.Nodes);
        Assert.Equal("2", r.Scalar);
    }
```

- [ ] **Step 2: Add a sequence-of-atomics test**

After the test above, add:

```csharp
    [Fact]
    public async Task EvaluateSequenceOfAtomicsRendersAsParenList()
    {
        var doc = ParseDoc(LibraryXml);

        var r = await Eval(doc, "1 to 5");

        Assert.Null(r.Error);
        Assert.Empty(r.Nodes);
        Assert.Equal("(1, 2, 3, 4, 5)", r.Scalar);
    }

    [Fact]
    public async Task EvaluateNodeResultLeavesScalarNull()
    {
        var doc = ParseDoc(LibraryXml);

        var r = await Eval(doc, "//book");

        Assert.Null(r.Error);
        Assert.Equal(2, r.Nodes.Count);
        Assert.Null(r.Scalar);
    }

    [Fact]
    public async Task EvaluateBooleanScalarRendersAsLowercase()
    {
        var doc = ParseDoc(LibraryXml);

        var r = await Eval(doc, "1 = 1");

        Assert.Null(r.Error);
        Assert.Empty(r.Nodes);
        Assert.Equal("true", r.Scalar);
    }

    [Fact]
    public async Task EvaluateLongSequenceTruncates()
    {
        var doc = ParseDoc(LibraryXml);

        var r = await Eval(doc, "1 to 100");

        Assert.Null(r.Error);
        Assert.Empty(r.Nodes);
        Assert.NotNull(r.Scalar);
        Assert.Contains("… +", r.Scalar);
        Assert.True(r.Scalar.Length <= 100, $"scalar exceeded soft cap: {r.Scalar.Length} chars");
    }
```

- [ ] **Step 3: Run the service tests**

Run: `dotnet test test/Phoenixml.Platform.Editor.Xml.Tests/Phoenixml.Platform.Editor.Xml.Tests.csproj --filter "FullyQualifiedName~XPathQueryServiceTests" --nologo 2>&1 | tail -5`
Expected: 12 / 12 passing (8 carried + 1 modified + 3 new).

---

### Task 4: Plumb `Scalar` through the controller

**Files:**
- Modify: `src/Phoenixml.Editor/Find/XPathQueryController.cs`

- [ ] **Step 1: Extend `IXPathQueryHost` with `SetXPathScalar`**

Inside `XPathQueryController.cs`, in the `IXPathQueryHost` interface, add this member after `SetXPathMatchCount`:

```csharp
    /// <summary>
    /// Sets the scalar XPath result for display in the status bar. <see langword="null"/>
    /// clears it so the selected-node XPath segment becomes visible again.
    /// </summary>
    /// <param name="scalar">Formatted scalar string, or <see langword="null"/> to clear.</param>
    void SetXPathScalar(string? scalar);
```

- [ ] **Step 2: Forward `Scalar` from `RefreshAsync`**

In `XPathQueryController.RefreshAsync`, locate the error-handling branch and the success branch. Replace the existing success tail (from `_matches = result.Nodes;` through the closing `_host.SetXPathMatchCount(...)` call) with:

```csharp
            _matches = result.Nodes;
            _activeIndex = _matches.Count > 0
                ? Math.Min(_activeIndex >= 0 ? _activeIndex : 0, _matches.Count - 1)
                : -1;
            UpdateIndex();
            if (_activeIndex >= 0) ShowActiveInTree();
            _host.SetXPathMatchCount(_activeIndex < 0 ? 0 : _activeIndex + 1, _matches.Count, error: null);
            _host.SetXPathScalar(result.Scalar);
```

In the `result.Error is not null` branch, after `_host.SetXPathMatchCount(0, 0, error: result.Error);` add:

```csharp
                _host.SetXPathScalar(null);
```

In the `_host.ActiveDocument is not XmlDocument doc` early return branch, after `_host.SetXPathMatchCount(0, 0, error: null);` add:

```csharp
                _host.SetXPathScalar(null);
```

- [ ] **Step 3: Clear scalar on close**

In `XPathQueryController.OnClose`, after the `_index.Reset([], null);` line and before `_host.ActiveProvider?.TriggerRefresh();`, add:

```csharp
        _host.SetXPathScalar(null);
```

- [ ] **Step 4: Build the controller**

Run: `dotnet build src/Phoenixml.Editor/Phoenixml.Editor.csproj --nologo 2>&1 | tail -10`
Expected: error in `EditorMainWindow` — `'EditorMainWindow' does not implement interface member 'IXPathQueryHost.SetXPathScalar(string?)'`. Drives Task 5.

---

### Task 5: Status-bar precedence in `EditorMainWindow`

**Files:**
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs`

- [ ] **Step 1: Add the scalar field**

After the `private string _selectedXPath = string.Empty;` declaration (around line 59), add:

```csharp
    /// <summary>
    /// Scalar XPath result from the query bar, when the active XPath expression yielded
    /// atomic values instead of nodes. Takes precedence over <see cref="_selectedXPath"/>
    /// in the status bar while the XPath bar is open. Cleared when the bar closes.
    /// </summary>
    private string _xpathScalar = string.Empty;
```

- [ ] **Step 2: Render scalar with precedence in `UpdateStatusBar`**

In `UpdateStatusBar()`, replace the `if (!string.IsNullOrEmpty(_selectedXPath))` block with:

```csharp
        // XPath bar's scalar result takes precedence over the selected-node XPath —
        // both reuse the same segment slot. Falls back to selected-node XPath when the
        // XPath bar is closed or returned nodes (not atomics).
        var xpathLabel = !string.IsNullOrEmpty(_xpathScalar) ? _xpathScalar : _selectedXPath;
        if (!string.IsNullOrEmpty(xpathLabel))
        {
            segments.Add(new StatusBarSegment(
                Id: "selected-xpath",
                Label: xpathLabel,
                Order: 2));
        }
```

- [ ] **Step 3: Implement `IXPathQueryHost.SetXPathScalar`**

At the bottom of `EditorMainWindow`, in the `IXPathQueryHost` block, after the `SetXPathMatchCount` impl add:

```csharp
    /// <inheritdoc/>
    void IXPathQueryHost.SetXPathScalar(string? scalar)
    {
        _xpathScalar = scalar ?? string.Empty;
        UpdateStatusBar();
    }
```

- [ ] **Step 4: Build the editor**

Run: `dotnet build src/Phoenixml.Editor/Phoenixml.Editor.csproj --nologo 2>&1 | tail -10`
Expected: build succeeds; 0 errors, 0 warnings.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test --nologo --no-build 2>&1 | tail -10`
Expected: all suites pass; new tests included.

---

### Task 6: Walkthrough doc

**Files:**
- Modify: `docs/editor-v0.12-walkthrough.md`

- [ ] **Step 1: Update status header**

Replace the existing **Status as of 2026-05-13** paragraph (top of file) with:

```markdown
**Status as of 2026-05-13:** v0.12 ships **Drag-and-drop reorder** of tree rows (Plan 19), **Breadcrumb + status-bar XPath** for the current selection (Plan 20), an **XPath query bar** (Plan 21), and **scalar XPath result display** (Plan 22). Drag any element row onto another to move it; drop **on** the row inserts as last child, drop **above** or **below** inserts as a preceding or following sibling. NodeId identity is preserved across the move, so selection and external references survive. Single undo per drag gesture. Selecting any node now shows its canonical XPath in the status bar and renders a clickable ancestor breadcrumb above the tree. **Ctrl+G** opens an XPath query bar above the tree — type an expression (e.g. `//book[year='1978']`), press Enter to highlight matching nodes via the same tinted-row machinery as Find. Expressions that return atomic values (e.g. `count(//book)`, `1 to 5`) render their serialized value in the status bar's XPath segment instead of tinting rows.
```

- [ ] **Step 2: Move "scalar display" from candidate to resolved**

Find the `## What's next: Plan 22 candidates` section. Add a `### Resolved 2026-05-13 (Plan 22)` block above the existing resolved sections (or in the "Resolved" list) — and remove `Scalar XPath result display` from the candidates list. Concretely:

In the "Resolved" sections near `### Resolved 2026-05-13 (Plan 21)`, add a new sibling:

```markdown
### Resolved 2026-05-13 (Plan 22)

- [x] ~~**Scalar XPath result display.**~~ — Expressions returning atomics (`count()`, `string-length()`, `1 to 5`, predicates that reduce to booleans, etc.) now render the serialized result in the status bar's XPath segment. Single atomic → bare value (`12`); multi → parenthesized comma list (`(1, 2, 3, 4, 5)`); long sequences truncate with `… +N more`. Boolean lowercase. Takes precedence over selected-node XPath while the bar is open; reverts on close.
```

In the candidates list, delete the first entry (`**Scalar XPath result display** — show \`count(//book) → 12\`...`).

- [ ] **Step 3: Add a Plan 22 architecture/smoke section**

Above `## What's next: Plan 22 candidates` (now Plan 23), add:

```markdown
## Plan 22 — XPath scalar result display

Read-only query feature; extends Plan 21 to surface atomic results.

- **Status-bar precedence** — while the XPath bar is open and the
  expression evaluates to atomics, the status bar's right-most segment
  shows the scalar instead of the selected-node XPath. Closing the bar
  (Escape) clears the scalar and restores the selected-node XPath.
- **Format conventions** — single value rendered bare (`12`, `true`,
  `Hello World`); sequences as `(a, b, c, …)` parenthesized,
  comma-separated, truncated at ~80 chars with `… +N more`. Boolean is
  lowercase. Invariant culture so locale doesn't drift the display.
- **Node-set still wins** — if the expression returns both nodes and
  atomics (rare in practice), the nodes are tinted and the scalar is
  suppressed. Atomics surface only when there are no nodes.

### Architecture pieces added in Plan 22

- **`XPathResult.Scalar`** in `Phoenixml.Platform.Editor.Xml.Find` —
  third field on the existing result record; non-null only when the
  expression yielded atomics and zero nodes.
- **`XPathQueryService.FormatAtomics`** — internal helper that
  stringifies a `IReadOnlyList<object?>` of boxed CLR primitives into
  the display string. Uses `Convert.ToString(item, CultureInfo.InvariantCulture)`
  with a `bool` → `"true"/"false"` shim.
- **`IXPathQueryHost.SetXPathScalar(string?)`** — new host callback;
  `EditorMainWindow` stores the value in `_xpathScalar` and calls
  `UpdateStatusBar`. The status-bar render picks `_xpathScalar` over
  `_selectedXPath` when non-empty.

### Smoke-test checklist (manual)

Load a multi-element XML doc. Test:

| Action | Expected | Actual |
|---|---|---|
| Ctrl+G, type `count(//book)`, Enter | Status bar shows `2` (or the count); no row tints; counter shows `no matches`. | tbd |
| Type `1 to 5`, Enter | Status bar shows `(1, 2, 3, 4, 5)`. | tbd |
| Type `1 to 100`, Enter | Status bar shows truncated `(1, 2, 3, … +N more)`, length ≤ ~80 chars. | tbd |
| Type `1 = 1`, Enter | Status bar shows `true`. | tbd |
| Type `//book/title/string()`, Enter | Status bar shows `(The C Programming Language, Effective Java)` (or your titles). | tbd |
| Switch to `//book`, Enter | Scalar clears, rows tint as before. | tbd |
| Escape (close bar) | Scalar clears; status bar reverts to the selected-node XPath (if any). | tbd |
| Select a tree row while scalar showing | Scalar still wins (bar is open). | tbd |
```

- [ ] **Step 4: Commit**

```bash
git add src/Phoenixml.Platform.Editor.Xml/Find/XPathQueryService.cs \
        src/Phoenixml.Editor/Find/XPathQueryController.cs \
        src/Phoenixml.Editor/EditorMainWindow.cs \
        test/Phoenixml.Platform.Editor.Xml.Tests/Find/XPathQueryServiceTests.cs \
        docs/editor-v0.12-walkthrough.md
git commit -m "$(cat <<'EOF'
feat(editor): scalar XPath result display (Plan 22)

Atomic-only XPath results now render in the status bar's XPath segment
instead of being silently dropped. Single atomic shows bare (`12`),
sequences as parenthesized comma list (`(1, 2, 3, 4, 5)`) truncated at
~80 chars with `… +N more`. Boolean lowercase, invariant culture.

XPathResult gains a Scalar field; XPathQueryService.FormatAtomics
stringifies boxed CLR primitives. Node-set still wins when both nodes
and atomics are returned. EditorMainWindow renders scalar with
precedence over the selected-node XPath while the bar is open;
closing the bar clears the scalar and restores the selected-node XPath.

4 new XPathQueryService tests (sequence, boolean, node-leaves-scalar-null,
truncation). Full suite still green.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Out of scope (deferred to later plans)

- XPath history dropdown (Plan 22 candidate, deferred)
- XPath function autocompletion
- Syntax highlighting inside the input
- Separate status segment for scalar (currently reuses `selected-xpath` slot)
- "Copy scalar to clipboard" affordance
