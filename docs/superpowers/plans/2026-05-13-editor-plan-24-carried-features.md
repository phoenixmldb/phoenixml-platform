# Editor v0.14 — Plan 24 Carried-Features Pack — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship all six v0.13 carried-forward features as one pack — dual-pane compare view, drop preview ghost, persistent XPath history, XPath parameter hints, context-aware XPath autocompletion, and namespace-aware XPath autocompletion. All MVP-scoped to keep the pack tractable.

**Architecture:**
- **Dual-pane compare:** new `CompareDocsWindow` hosting two `NodeListView` instances side-by-side. Tints rows by diff status via a new `DiffIndex` injected into each provider (parallel to `MatchIndex`). Scroll sync via Avalonia `ScrollViewer.Offset` two-way binding. Reuses `XmlDocumentDiff` from Plan 23.
- **Drop preview ghost:** transient `Popup` positioned at cursor during `DragOver`, showing the dragged element's label. No adorner-layer machinery.
- **Persistent XPath history:** `XPathHistoryStore` reads/writes `~/.config/phoenixml/xpath-history.txt` (newline-delimited). Loaded on editor startup; saved after each `Record`.
- **Parameter hints:** `XPathSignatures` static map (name → signature string). Popup appears when caret moves to just after `(` of a known function.
- **Context-aware autocompletion:** `XPathContextProvider.DetectContext(text, caret)` returns one of `{ FunctionName, ElementName, AttributeName, None }` based on token prefix. Element/attribute names harvested from the active document via a lightweight walker. `XPathQueryBar` calls the provider on TextChanged and routes the popup population through the right source.
- **Namespace-aware autocompletion:** `XPathFunctionList` partitioned by namespace prefix. Typing `map:` filters to map functions, `xs:` to schema types, etc.

**Tech Stack:** Avalonia 11.2.4, PhoenixmlDb.Core, existing `XmlDocumentDiff` + `XPathBuilder`.

**Scope discipline:** Dual-pane compare is the biggest item. MVP excludes: keyboard navigation between diffs, structural row alignment (rows display in each doc's natural order; diff tint identifies them), three-way merge, in-line diff editing. Future plans can add those.

---

## File structure

| File | Role |
|---|---|
| `src/Phoenixml.Platform.Editor.Xml/Diff/DiffIndex.cs` | NEW — runtime tint provider for diff-highlighting in NodeListView (parallel to `MatchIndex`) |
| `src/Phoenixml.Platform.Editor.Xml/Diff/DiffIndexBuilder.cs` | NEW — builds a `DiffIndex` from an `XmlDiffResult` + `XmlDocument` |
| `src/Phoenixml.Editor/Compare/CompareDocsWindow.axaml(.cs)` | NEW — two `NodeListView` side-by-side, synced scroll |
| `src/Phoenixml.Platform/Editing/NodeListView.axaml.cs` | Modify — expose `DiffIndex` setter; tint hook in `NodeRowViewModel` |
| `src/Phoenixml.Platform/Editing/NodeListView.axaml.cs` | Modify — drop preview ghost popup |
| `src/Phoenixml.Editor/EditorMainWindow.cs` | Modify — `CompareWithFileAsync` opens dual-pane view; history persistence on startup/shutdown |
| `src/Phoenixml.Editor/Find/XPathHistoryStore.cs` | NEW — load/save history to disk |
| `src/Phoenixml.Editor/Find/XPathHistory.cs` | Modify — `Load(IEnumerable<string>)` + `Save()` hook |
| `src/Phoenixml.Editor/Find/XPathSignatures.cs` | NEW — function name → signature string map |
| `src/Phoenixml.Editor/Find/XPathContextProvider.cs` | NEW — caret-context detection + name harvest |
| `src/Phoenixml.Editor/Find/XPathFunctionList.cs` | Modify — namespace buckets (`fn:` / `map:` / `array:` / `xs:`) |
| `src/Phoenixml.Editor/Find/XPathQueryBar.axaml(.cs)` | Modify — signature popup, context routing |
| `test/Phoenixml.Platform.Editor.Xml.Tests/Diff/DiffIndexBuilderTests.cs` | NEW — 4 tests |
| `test/Phoenixml.Editor.Tests/Find/XPathHistoryStoreTests.cs` | NEW — 3 tests |
| `test/Phoenixml.Editor.Tests/Find/XPathSignaturesTests.cs` | NEW — 3 tests |
| `test/Phoenixml.Editor.Tests/Find/XPathContextProviderTests.cs` | NEW — 6 tests |
| `test/Phoenixml.Editor.Tests/Find/XPathFunctionListNamespaceTests.cs` | NEW — 4 tests |
| `docs/editor-v0.14-walkthrough.md` | NEW — v0.14 walkthrough |

---

## Tasks

### Task 1: `DiffIndex` + `DiffIndexBuilder`

**Files:**
- Create: `src/Phoenixml.Platform.Editor.Xml/Diff/DiffIndex.cs`
- Create: `src/Phoenixml.Platform.Editor.Xml/Diff/DiffIndexBuilder.cs`
- Create: `test/Phoenixml.Platform.Editor.Xml.Tests/Diff/DiffIndexBuilderTests.cs`

- [ ] **Step 1: Write the builder tests**

Create `test/Phoenixml.Platform.Editor.Xml.Tests/Diff/DiffIndexBuilderTests.cs`:

```csharp
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml.Diff;
using Phoenixml.Platform.Editor.Xml.Navigation;
using Xunit;

namespace Phoenixml.Platform.Editor.Xml.Tests.Diff;

public class DiffIndexBuilderTests
{
    private static XmlDocument Parse(string xml)
    {
        var (result, nsTable) = XmlDocument.ParseXml(xml);
        return new XmlDocument(
            Phoenixml.Platform.Abstractions.Documents.DocumentId.New(),
            source: null, displayName: "doc.xml", result, nsTable);
    }

    [Fact]
    public void IdenticalDocsProduceEmptyIndex()
    {
        var a = Parse("<r><x/></r>");
        var b = Parse("<r><x/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        var leftIdx = DiffIndexBuilder.Build(a, diff, side: DiffSide.Left);
        Assert.Equal(0, leftIdx.Count);
    }

    [Fact]
    public void RemovedElementShowsLeftSideOnly()
    {
        var a = Parse("<r><x/><y/></r>");
        var b = Parse("<r><x/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        var leftIdx = DiffIndexBuilder.Build(a, diff, side: DiffSide.Left);
        var rightIdx = DiffIndexBuilder.Build(b, diff, side: DiffSide.Right);

        var yId = ResolveByXPath(a, "/r/y");
        Assert.Equal(DiffKind.Removed, leftIdx.GetKind(yId));
        Assert.Equal(1, leftIdx.Count);
        Assert.Equal(0, rightIdx.Count);
    }

    [Fact]
    public void AddedElementShowsRightSideOnly()
    {
        var a = Parse("<r><x/></r>");
        var b = Parse("<r><x/><z/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        var leftIdx = DiffIndexBuilder.Build(a, diff, side: DiffSide.Left);
        var rightIdx = DiffIndexBuilder.Build(b, diff, side: DiffSide.Right);

        var zId = ResolveByXPath(b, "/r/z");
        Assert.Equal(DiffKind.Added, rightIdx.GetKind(zId));
        Assert.Equal(0, leftIdx.Count);
        Assert.Equal(1, rightIdx.Count);
    }

    [Fact]
    public void ChangedAttributeShowsBothSides()
    {
        var a = Parse("<r><x a=\"1\"/></r>");
        var b = Parse("<r><x a=\"2\"/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        var leftIdx = DiffIndexBuilder.Build(a, diff, side: DiffSide.Left);
        var rightIdx = DiffIndexBuilder.Build(b, diff, side: DiffSide.Right);

        var aLeft = ResolveByXPath(a, "/r/x/@a");
        var aRight = ResolveByXPath(b, "/r/x/@a");
        Assert.Equal(DiffKind.Changed, leftIdx.GetKind(aLeft));
        Assert.Equal(DiffKind.Changed, rightIdx.GetKind(aRight));
    }

    private static PhoenixmlDb.Core.NodeId ResolveByXPath(XmlDocument doc, string xpath)
    {
        // Walk every node, find first whose canonical XPath matches.
        foreach (var node in EnumerateAll(doc))
        {
            if (XPathBuilder.Build(doc, node.Id) == xpath)
                return node.Id;
        }
        throw new Xunit.Sdk.XunitException($"No node at {xpath}");
    }

    private static System.Collections.Generic.IEnumerable<XdmNode> EnumerateAll(XmlDocument doc)
    {
        var stack = new System.Collections.Generic.Stack<PhoenixmlDb.Core.NodeId>();
        stack.Push(doc.Root.Id);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            var node = doc.Resolve(id);
            if (node is null) continue;
            yield return node;
            if (node is XdmElement el)
            {
                foreach (var a in el.Attributes) stack.Push(a);
                foreach (var c in el.Children) stack.Push(c);
            }
            else if (node is XdmDocument d)
            {
                foreach (var c in d.Children) stack.Push(c);
            }
        }
    }
}
```

- [ ] **Step 2: Run tests to verify red**

Run: `dotnet test test/Phoenixml.Platform.Editor.Xml.Tests/Phoenixml.Platform.Editor.Xml.Tests.csproj --filter "FullyQualifiedName~DiffIndexBuilderTests" --nologo 2>&1 | tail -3`
Expected: build fails — types not found.

- [ ] **Step 3: Implement `DiffIndex`**

Create `src/Phoenixml.Platform.Editor.Xml/Diff/DiffIndex.cs`:

```csharp
using System.Collections.Generic;
using PhoenixmlDb.Core;

namespace Phoenixml.Platform.Editor.Xml.Diff;

/// <summary>Which side of a comparison this index represents.</summary>
public enum DiffSide { Left, Right }

/// <summary>
/// Runtime lookup for diff-tinting in <c>NodeListView</c>. Parallel to <c>MatchIndex</c>
/// from Plan 15. Maps <see cref="NodeId"/> → <see cref="DiffKind"/> so the tree provider
/// can paint added/removed/changed rows with distinct background colors.
/// </summary>
public sealed class DiffIndex
{
    private readonly Dictionary<NodeId, DiffKind> _byId = new();

    /// <summary>Number of tinted nodes.</summary>
    public int Count => _byId.Count;

    /// <summary>Returns the diff kind for <paramref name="id"/>, or <see langword="null"/>.</summary>
    public DiffKind? GetKind(NodeId id) =>
        _byId.TryGetValue(id, out var k) ? k : null;

    /// <summary>Replaces the index contents.</summary>
    public void Reset(IReadOnlyDictionary<NodeId, DiffKind> entries)
    {
        _byId.Clear();
        foreach (var (id, kind) in entries)
            _byId[id] = kind;
    }
}
```

- [ ] **Step 4: Implement `DiffIndexBuilder`**

Create `src/Phoenixml.Platform.Editor.Xml/Diff/DiffIndexBuilder.cs`:

```csharp
using System.Collections.Generic;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml.Navigation;

namespace Phoenixml.Platform.Editor.Xml.Diff;

/// <summary>
/// Resolves <see cref="DiffChange"/>s back to <see cref="NodeId"/>s in a specific
/// document so the dual-pane compare view can tint rows by diff kind.
/// </summary>
/// <remarks>
/// The diff is keyed by canonical XPath. This builder walks the document, computes
/// each node's XPath, and stamps the matching diff kind into a <see cref="DiffIndex"/>.
/// Per side: left-side nodes are stamped for Removed and Changed; right-side nodes
/// are stamped for Added and Changed. (A change shows on both sides.)
/// </remarks>
public static class DiffIndexBuilder
{
    /// <summary>Builds the index for one side of a comparison.</summary>
    public static DiffIndex Build(XmlDocument doc, XmlDiffResult diff, DiffSide side)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(diff);

        // Pre-collect the diff entries we care about for this side, keyed by xpath.
        var wanted = new Dictionary<string, DiffKind>(StringComparer.Ordinal);
        foreach (var change in diff.Changes)
        {
            var include = (side, change.Kind) switch
            {
                (DiffSide.Left, DiffKind.Removed) => true,
                (DiffSide.Left, DiffKind.Changed) => true,
                (DiffSide.Right, DiffKind.Added) => true,
                (DiffSide.Right, DiffKind.Changed) => true,
                _ => false,
            };
            if (include) wanted[change.XPath] = change.Kind;
        }

        var entries = new Dictionary<NodeId, DiffKind>();
        if (wanted.Count == 0)
        {
            var empty = new DiffIndex();
            empty.Reset(entries);
            return empty;
        }

        Walk(doc, doc.Root.Id, wanted, entries);
        var idx = new DiffIndex();
        idx.Reset(entries);
        return idx;
    }

    private static void Walk(
        XmlDocument doc,
        NodeId id,
        IReadOnlyDictionary<string, DiffKind> wanted,
        Dictionary<NodeId, DiffKind> entries)
    {
        var node = doc.Resolve(id);
        if (node is null) return;

        if (node is XdmElement el)
        {
            var xpath = XPathBuilder.Build(doc, id);
            if (wanted.TryGetValue(xpath, out var kind))
                entries[id] = kind;

            foreach (var attrId in el.Attributes)
            {
                if (doc.Resolve(attrId) is XdmAttribute)
                {
                    var attrXpath = XPathBuilder.Build(doc, attrId);
                    if (wanted.TryGetValue(attrXpath, out var attrKind))
                        entries[attrId] = attrKind;
                }
            }

            foreach (var childId in el.Children)
                Walk(doc, childId, wanted, entries);
        }
        else if (node is XdmDocument d)
        {
            foreach (var childId in d.Children)
                Walk(doc, childId, wanted, entries);
        }
    }
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test test/Phoenixml.Platform.Editor.Xml.Tests/Phoenixml.Platform.Editor.Xml.Tests.csproj --filter "FullyQualifiedName~DiffIndexBuilderTests" --nologo 2>&1 | tail -5`
Expected: 4 / 4 passing.

---

### Task 2: Wire `DiffIndex` into `NodeListView` row tinting

**Files:**
- Modify: `src/Phoenixml.Platform/Editing/NodeListView.axaml.cs`
- Modify: `src/Phoenixml.Platform/Editing/NodeRowViewModel.cs` (or wherever row VMs live)

**Approach:** Add a `DiffIndex` setter on `NodeListView`. When set, each row's `MatchHighlightBrush` becomes the diff-tint brush instead of the find-match brush (find and diff are mutually exclusive in the compare window). For the editor's normal NodeListView, `DiffIndex` is null and behaviour is unchanged.

- [ ] **Step 1: Find the existing row tint binding**

Run: `grep -n "MatchHighlightBrush\|MatchIndex" src/Phoenixml.Platform/Editing/NodeRowViewModel.cs 2>/dev/null || grep -rn "MatchHighlightBrush" src/Phoenixml.Platform/Editing/`
Use the output to locate the brush computation. It should be a getter that consults `MatchIndex` and returns the active/inactive/no-match brush.

- [ ] **Step 2: Add `DiffIndex` to the row VM dependency**

Wherever the row VM holds a `MatchIndex` reference (likely via the provider), add a parallel optional `DiffIndex` reference. Compute the brush as:

```csharp
public IBrush MatchHighlightBrush
{
    get
    {
        if (_diffIndex?.GetKind(Node.Id) is DiffKind kind)
        {
            return kind switch
            {
                DiffKind.Added   => DiffBrushes.Added,
                DiffKind.Removed => DiffBrushes.Removed,
                DiffKind.Changed => DiffBrushes.Changed,
                _ => Brushes.Transparent,
            };
        }
        // Fall through to existing MatchIndex logic.
        ...
    }
}
```

Add a `DiffBrushes` static class near the top of the file (or in a new `Theme/DiffBrushes.cs`):

```csharp
internal static class DiffBrushes
{
    public static readonly IBrush Added = new SolidColorBrush(Color.Parse("#1F4F2A"));      // muted green
    public static readonly IBrush Removed = new SolidColorBrush(Color.Parse("#5A1F1F"));    // muted red
    public static readonly IBrush Changed = new SolidColorBrush(Color.Parse("#5A4F1F"));    // muted yellow
}
```

- [ ] **Step 3: Expose a `DiffIndex` setter on the provider**

`XmlTreeNodeProvider` is the entry point. Add:

```csharp
public DiffIndex? DiffIndex { get; set; }
```

And propagate it into each row VM constructor (same shape as `MatchIndex`).

- [ ] **Step 4: Build + run existing tests**

Run: `dotnet test --nologo --no-build 2>&1 | tail -5`
Expected: green; the new field is null in the editor's normal NodeListView so existing behaviour is unchanged.

---

### Task 3: `CompareDocsWindow` (dual-pane view)

**Files:**
- Create: `src/Phoenixml.Editor/Compare/CompareDocsWindow.axaml`
- Create: `src/Phoenixml.Editor/Compare/CompareDocsWindow.axaml.cs`
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs` (route Compare With… to dual-pane window)

- [ ] **Step 1: Create the AXAML**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:editing="clr-namespace:Phoenixml.Platform.Editing;assembly=Phoenixml.Platform"
        x:Class="Phoenixml.Editor.Compare.CompareDocsWindow"
        Width="1100" Height="700"
        Title="Compare">
    <DockPanel LastChildFill="True">
        <Border DockPanel.Dock="Top" Background="#252526" Padding="8,4">
            <TextBlock Name="HeaderText" Foreground="#CCCCCC" FontFamily="Cascadia Mono, Consolas, monospace" />
        </Border>
        <Grid ColumnDefinitions="*,1,*">
            <editing:NodeListView Name="LeftPane" Grid.Column="0" />
            <Border Grid.Column="1" Background="#4A4A4F" />
            <editing:NodeListView Name="RightPane" Grid.Column="2" />
        </Grid>
    </DockPanel>
</Window>
```

- [ ] **Step 2: Create the code-behind**

```csharp
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PhoenixmlDb.Core;
using Phoenixml.Platform.Editing;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Diff;
using Phoenixml.Platform.Editor.Xml.Tree;

namespace Phoenixml.Editor.Compare;

/// <summary>
/// Modal-style window showing two documents side-by-side with diff-tinted rows.
/// Synced scrolling: when one pane scrolls, the other scrolls to the same vertical
/// offset. Find / XPath / edit interactions on each pane are disabled — read-only view.
/// </summary>
public sealed partial class CompareDocsWindow : Window
{
    public CompareDocsWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Loads two documents, computes their diff, builds per-side <see cref="DiffIndex"/>es,
    /// and binds the panes. Call before <see cref="Window.Show"/>.
    /// </summary>
    public void Load(XmlDocument left, XmlDocument right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var diff = XmlDocumentDiff.Compute(left, right);
        var header = this.FindControl<TextBlock>("HeaderText");
        if (header is not null)
            header.Text = $"{left.DisplayName} ↔ {right.DisplayName} — {diff.Changes.Count} difference(s)";

        var leftIdx = DiffIndexBuilder.Build(left, diff, DiffSide.Left);
        var rightIdx = DiffIndexBuilder.Build(right, diff, DiffSide.Right);

        var leftProvider = (XmlTreeNodeProvider)left.TreeNodes;
        var rightProvider = (XmlTreeNodeProvider)right.TreeNodes;
        leftProvider.DiffIndex = leftIdx;
        rightProvider.DiffIndex = rightIdx;

        var leftPane = this.FindControl<NodeListView>("LeftPane")!;
        var rightPane = this.FindControl<NodeListView>("RightPane")!;
        leftPane.SetProvider(leftProvider);
        rightPane.SetProvider(rightProvider);

        WireSyncedScroll(leftPane, rightPane);
    }

    private static void WireSyncedScroll(NodeListView a, NodeListView b)
    {
        // Each NodeListView wraps a ListBox. Find the inner ScrollViewer and forward Offset.
        var aHost = a.FindControl<ListBox>("RowsHost");
        var bHost = b.FindControl<ListBox>("RowsHost");
        if (aHost?.Scroll is null || bHost?.Scroll is null) return;

        var aScroll = aHost.Scroll;
        var bScroll = bHost.Scroll;
        var muting = false;

        aScroll.ScrollChanged += (_, _) =>
        {
            if (muting) return;
            muting = true;
            try { bScroll.Offset = bScroll.Offset.WithY(aScroll.Offset.Y); }
            finally { muting = false; }
        };
        bScroll.ScrollChanged += (_, _) =>
        {
            if (muting) return;
            muting = true;
            try { aScroll.Offset = aScroll.Offset.WithY(bScroll.Offset.Y); }
            finally { muting = false; }
        };
    }
}
```

- [ ] **Step 3: Route `CompareWithFileAsync` to the dual-pane view**

In `EditorMainWindow.cs`, replace the existing `ShowDiffReport(...)` call inside `CompareWithFileAsync` with:

```csharp
        var window = new Compare.CompareDocsWindow();
        window.Load(left, right);
        window.Show(_window);
```

Delete the `ShowDiffReport` method (no longer used).

- [ ] **Step 4: Build + smoke**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: 0 errors.

No unit tests for the window itself — coverage comes via `DiffIndexBuilderTests` and manual smoke.

---

### Task 4: Drop preview ghost

**Files:**
- Modify: `src/Phoenixml.Platform/Editing/NodeListView.axaml`
- Modify: `src/Phoenixml.Platform/Editing/NodeListView.axaml.cs`

**Approach:** A `Popup` anchored to the `NodeListView` itself, shown during drag, positioned manually via `HorizontalOffset` / `VerticalOffset` updated in `OnRowDragOver`. Displays the dragged element's label.

- [ ] **Step 1: Add the popup to AXAML**

In `NodeListView.axaml`, just above `</UserControl>`'s closing tag:

```xml
    <Popup Name="DragGhostPopup"
           PlacementTarget="{Binding $self}"
           Placement="Pointer"
           IsHitTestVisible="False">
        <Border Background="#252526" BorderBrush="#1E90FF" BorderThickness="1"
                Padding="6,2" CornerRadius="3">
            <TextBlock Name="DragGhostText"
                       Foreground="#CCCCCC"
                       FontFamily="Cascadia Mono, Consolas, monospace" />
        </Border>
    </Popup>
```

Wait — `Popup` is not a child of `ListBox`. Wrap the `ListBox` and `Popup` in a single root `Panel`. Current root is `<ListBox Name="RowsHost">`. Restructure:

```xml
<UserControl ...>
    <Panel>
        <ListBox Name="RowsHost" ... > ... </ListBox>
        <Popup Name="DragGhostPopup" ... > ... </Popup>
    </Panel>
</UserControl>
```

- [ ] **Step 2: Wire show/hide in code-behind**

In `NodeListView.axaml.cs`, in `StartDragAsync`, before `await DragDrop.DoDragDrop(...)`, show the popup:

```csharp
        var ghostPopup = this.FindControl<Popup>("DragGhostPopup");
        var ghostText = this.FindControl<TextBlock>("DragGhostText");
        if (ghostPopup is not null && ghostText is not null)
        {
            ghostText.Text = $"Move <{dragRow.Label}>";
            ghostPopup.IsOpen = true;
        }
```

In the `finally` block, hide it:

```csharp
            if (ghostPopup is not null) ghostPopup.IsOpen = false;
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Phoenixml.Platform/Phoenixml.Platform.csproj --nologo 2>&1 | tail -5`
Expected: 0 errors.

Smoke is manual — covered by Task 7's walkthrough table.

---

### Task 5: Persistent XPath history

**Files:**
- Create: `src/Phoenixml.Editor/Find/XPathHistoryStore.cs`
- Modify: `src/Phoenixml.Editor/Find/XPathHistory.cs`
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs` (load on startup, save on close)
- Create: `test/Phoenixml.Editor.Tests/Find/XPathHistoryStoreTests.cs`

- [ ] **Step 1: Write the store tests**

Create `test/Phoenixml.Editor.Tests/Find/XPathHistoryStoreTests.cs`:

```csharp
using System;
using System.IO;
using Phoenixml.Editor.Find;
using Xunit;

namespace Phoenixml.Editor.Tests.Find;

public class XPathHistoryStoreTests
{
    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"phoenixml-test-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(dir, "history.txt");
            XPathHistoryStore.Save(path, ["//a", "//b", "//c"]);
            var loaded = XPathHistoryStore.Load(path);
            Assert.Equal(["//a", "//b", "//c"], loaded);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadReturnsEmptyWhenFileMissing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"phoenixml-missing-{Guid.NewGuid():N}.txt");
        var loaded = XPathHistoryStore.Load(missing);
        Assert.Empty(loaded);
    }

    [Fact]
    public void SaveSkipsBlankLines()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"phoenixml-test-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(dir, "history.txt");
            File.Create(path).Dispose();
            File.WriteAllText(path, "//a\n\n   \n//b\n");
            var loaded = XPathHistoryStore.Load(path);
            Assert.Equal(["//a", "//b"], loaded);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Implement `XPathHistoryStore`**

Create `src/Phoenixml.Editor/Find/XPathHistoryStore.cs`:

```csharp
using System.Collections.Generic;
using System.IO;

namespace Phoenixml.Editor.Find;

/// <summary>
/// On-disk persistence for <see cref="XPathHistory"/>. Plain-text, newline-delimited;
/// blank lines are ignored on load. Stored under
/// <c>~/.config/phoenixml/xpath-history.txt</c> by default.
/// </summary>
public static class XPathHistoryStore
{
    /// <summary>The default user-config path for the history file.</summary>
    public static string DefaultPath
    {
        get
        {
            var home = System.Environment.GetEnvironmentVariable("HOME")
                ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "phoenixml", "xpath-history.txt");
        }
    }

    /// <summary>
    /// Loads entries from <paramref name="path"/>. Returns an empty list when the file is
    /// missing; blank lines are skipped.
    /// </summary>
    public static IReadOnlyList<string> Load(string path)
    {
        if (!File.Exists(path)) return [];
        var result = new List<string>();
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                result.Add(trimmed);
        }
        return result;
    }

    /// <summary>Writes <paramref name="entries"/> to <paramref name="path"/>, one per line.</summary>
    public static void Save(string path, IReadOnlyList<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllLines(path, entries);
    }
}
```

- [ ] **Step 3: Extend `XPathHistory` to seed from a list**

In `XPathHistory.cs`, after `Record`:

```csharp
    /// <summary>Seeds the history from a list (in most-recent-first order).</summary>
    /// <remarks>
    /// Replaces any existing entries. Items beyond <see cref="Capacity"/> are dropped.
    /// </remarks>
    public void LoadFrom(IReadOnlyList<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries.Clear();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            _entries.Add(entry);
            if (_entries.Count >= Capacity) break;
        }
    }
```

- [ ] **Step 4: Wire into `EditorMainWindow`**

In `EditorMainWindow` constructor, after `_xpathController = new XPathQueryController(...)`:

```csharp
        // Load persisted XPath history.
        try
        {
            var entries = XPathHistoryStore.Load(XPathHistoryStore.DefaultPath);
            // Access the controller's internal history via a new method (Task 5 adds it).
            _xpathController.LoadHistory(entries);
        }
        catch (IOException) { /* permission or disk — silent */ }
```

Add `LoadHistory` to `XPathQueryController`:

```csharp
    public void LoadHistory(IReadOnlyList<string> entries) => _history.LoadFrom(entries);
```

For save, hook the Avalonia application exit. In the constructor, after registering events:

```csharp
        _window.Closing += (_, _) =>
        {
            try
            {
                XPathHistoryStore.Save(XPathHistoryStore.DefaultPath, _xpathController.SnapshotHistory());
            }
            catch (IOException) { /* silent */ }
        };
```

- [ ] **Step 5: Run all history tests**

Run: `dotnet test test/Phoenixml.Editor.Tests/Phoenixml.Editor.Tests.csproj --filter "FullyQualifiedName~XPathHistory" --nologo 2>&1 | tail -5`
Expected: 4 + 3 = 7 tests passing.

---

### Task 6: XPath parameter hints (`XPathSignatures`)

**Files:**
- Create: `src/Phoenixml.Editor/Find/XPathSignatures.cs`
- Create: `test/Phoenixml.Editor.Tests/Find/XPathSignaturesTests.cs`
- Modify: `src/Phoenixml.Editor/Find/XPathQueryBar.axaml` (add second popup for signature)
- Modify: `src/Phoenixml.Editor/Find/XPathQueryBar.axaml.cs` (show signature when caret is just past `(`)

- [ ] **Step 1: Write signature tests**

Create `test/Phoenixml.Editor.Tests/Find/XPathSignaturesTests.cs`:

```csharp
using Phoenixml.Editor.Find;
using Xunit;

namespace Phoenixml.Editor.Tests.Find;

public class XPathSignaturesTests
{
    [Fact]
    public void ReturnsNullForUnknownFunction()
    {
        Assert.Null(XPathSignatures.Get("not-a-real-fn"));
    }

    [Fact]
    public void ReturnsSignatureForCommonFunction()
    {
        Assert.Equal("count($arg as item()*) as xs:integer", XPathSignatures.Get("count"));
    }

    [Fact]
    public void IsCaseSensitive()
    {
        // XPath function names are case-sensitive.
        Assert.Null(XPathSignatures.Get("Count"));
        Assert.NotNull(XPathSignatures.Get("count"));
    }
}
```

- [ ] **Step 2: Implement signatures**

Create `src/Phoenixml.Editor/Find/XPathSignatures.cs`:

```csharp
using System.Collections.Generic;

namespace Phoenixml.Editor.Find;

/// <summary>
/// Curated map of XPath 3.1 <c>fn:*</c> function signatures (local name → signature string).
/// MVP: covers the most-common 30 functions. Function names are case-sensitive per XPath.
/// </summary>
public static class XPathSignatures
{
    private static readonly Dictionary<string, string> Map = new(System.StringComparer.Ordinal)
    {
        ["abs"] = "abs($arg as xs:numeric?) as xs:numeric?",
        ["boolean"] = "boolean($arg as item()*) as xs:boolean",
        ["ceiling"] = "ceiling($arg as xs:numeric?) as xs:numeric?",
        ["concat"] = "concat($arg1, $arg2, …) as xs:string",
        ["contains"] = "contains($arg1 as xs:string?, $arg2 as xs:string?) as xs:boolean",
        ["count"] = "count($arg as item()*) as xs:integer",
        ["data"] = "data($arg as item()*) as xs:anyAtomicType*",
        ["distinct-values"] = "distinct-values($arg as xs:anyAtomicType*) as xs:anyAtomicType*",
        ["doc"] = "doc($uri as xs:string?) as document-node()?",
        ["empty"] = "empty($arg as item()*) as xs:boolean",
        ["ends-with"] = "ends-with($arg1 as xs:string?, $arg2 as xs:string?) as xs:boolean",
        ["exists"] = "exists($arg as item()*) as xs:boolean",
        ["false"] = "false() as xs:boolean",
        ["floor"] = "floor($arg as xs:numeric?) as xs:numeric?",
        ["head"] = "head($arg as item()*) as item()?",
        ["last"] = "last() as xs:integer",
        ["local-name"] = "local-name($arg as node()?) as xs:string",
        ["lower-case"] = "lower-case($arg as xs:string?) as xs:string",
        ["matches"] = "matches($input as xs:string?, $pattern as xs:string) as xs:boolean",
        ["max"] = "max($arg as xs:anyAtomicType*) as xs:anyAtomicType?",
        ["min"] = "min($arg as xs:anyAtomicType*) as xs:anyAtomicType?",
        ["name"] = "name($arg as node()?) as xs:string",
        ["namespace-uri"] = "namespace-uri($arg as node()?) as xs:anyURI",
        ["normalize-space"] = "normalize-space($arg as xs:string?) as xs:string",
        ["not"] = "not($arg as item()*) as xs:boolean",
        ["number"] = "number($arg as xs:anyAtomicType?) as xs:double",
        ["position"] = "position() as xs:integer",
        ["replace"] = "replace($input as xs:string?, $pattern as xs:string, $replacement as xs:string) as xs:string",
        ["round"] = "round($arg as xs:numeric?) as xs:numeric?",
        ["starts-with"] = "starts-with($arg1 as xs:string?, $arg2 as xs:string?) as xs:boolean",
        ["string"] = "string($arg as item()?) as xs:string",
        ["string-join"] = "string-join($arg as xs:anyAtomicType*, $sep as xs:string?) as xs:string",
        ["string-length"] = "string-length($arg as xs:string?) as xs:integer",
        ["substring"] = "substring($source as xs:string?, $start as xs:double, $length as xs:double?) as xs:string",
        ["sum"] = "sum($arg as xs:anyAtomicType*, $zero as xs:anyAtomicType?) as xs:anyAtomicType?",
        ["tokenize"] = "tokenize($input as xs:string?, $pattern as xs:string) as xs:string*",
        ["true"] = "true() as xs:boolean",
        ["upper-case"] = "upper-case($arg as xs:string?) as xs:string",
    };

    /// <summary>Returns the signature string for <paramref name="name"/>, or null.</summary>
    public static string? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Map.TryGetValue(name, out var sig) ? sig : null;
    }
}
```

- [ ] **Step 3: Run signature tests**

Run: `dotnet test test/Phoenixml.Editor.Tests/Phoenixml.Editor.Tests.csproj --filter "FullyQualifiedName~XPathSignaturesTests" --nologo 2>&1 | tail -3`
Expected: 3 / 3 passing.

- [ ] **Step 4: Add the signature popup to AXAML**

In `XPathQueryBar.axaml`, inside the input `Panel`, alongside the existing completion popup, add:

```xml
                <Popup Name="SignaturePopup"
                       PlacementTarget="{Binding ElementName=QueryBox}"
                       Placement="TopEdgeAlignedLeft"
                       IsHitTestVisible="False">
                    <Border Background="#252526" BorderBrush="#4A4A4F" BorderThickness="1" Padding="6,2">
                        <TextBlock Name="SignatureText"
                                   Foreground="#9CDCFE"
                                   FontFamily="Cascadia Mono, Consolas, monospace" />
                    </Border>
                </Popup>
```

- [ ] **Step 5: Wire signature detection in code-behind**

In `XPathQueryBar.axaml.cs`, after the existing `queryBox.TextChanged` handler, add:

```csharp
        var signaturePopup = this.FindControl<Popup>("SignaturePopup")!;
        var signatureText = this.FindControl<TextBlock>("SignatureText")!;

        queryBox.TextChanged += (_, _) =>
        {
            var fnName = FunctionNameBeforeOpenParen(queryBox.Text, queryBox.CaretIndex);
            if (fnName is not null && XPathSignatures.Get(fnName) is string sig)
            {
                signatureText.Text = sig;
                signaturePopup.IsOpen = true;
            }
            else
            {
                signaturePopup.IsOpen = false;
            }
        };
```

Add the helper:

```csharp
    private static string? FunctionNameBeforeOpenParen(string? text, int caret)
    {
        if (string.IsNullOrEmpty(text) || caret == 0) return null;
        // Find the nearest unmatched '(' to the left of caret, then read the function name
        // immediately before it.
        int depth = 0;
        int parenIndex = -1;
        for (int i = caret - 1; i >= 0; i--)
        {
            var c = text[i];
            if (c == ')') depth++;
            else if (c == '(')
            {
                if (depth == 0) { parenIndex = i; break; }
                depth--;
            }
        }
        if (parenIndex <= 0) return null;
        int start = parenIndex;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        if (start == parenIndex) return null;
        return text.Substring(start, parenIndex - start);
    }
```

- [ ] **Step 6: Build + verify both popups coexist**

Run: `dotnet build src/Phoenixml.Editor/Phoenixml.Editor.csproj --nologo 2>&1 | tail -5`
Expected: 0 errors.

---

### Task 7: Context-aware autocompletion

**Files:**
- Create: `src/Phoenixml.Editor/Find/XPathContextProvider.cs`
- Create: `test/Phoenixml.Editor.Tests/Find/XPathContextProviderTests.cs`
- Modify: `src/Phoenixml.Editor/Find/XPathQueryBar.axaml.cs` (route completion source by context)
- Modify: `src/Phoenixml.Editor/Find/XPathQueryController.cs` (expose `HarvestNames` callback for the bar)

- [ ] **Step 1: Write context-detection tests**

Create `test/Phoenixml.Editor.Tests/Find/XPathContextProviderTests.cs`:

```csharp
using Phoenixml.Editor.Find;
using Xunit;

namespace Phoenixml.Editor.Tests.Find;

public class XPathContextProviderTests
{
    [Fact]
    public void EmptyInputReturnsNone()
    {
        var ctx = XPathContextProvider.DetectContext("", 0);
        Assert.Equal(XPathContextKind.None, ctx.Kind);
        Assert.Equal("", ctx.Prefix);
    }

    [Fact]
    public void AfterDoubleSlashIsElementName()
    {
        var ctx = XPathContextProvider.DetectContext("//", 2);
        Assert.Equal(XPathContextKind.ElementName, ctx.Kind);
        Assert.Equal("", ctx.Prefix);
    }

    [Fact]
    public void PartialElementNameAfterSlash()
    {
        var ctx = XPathContextProvider.DetectContext("//boo", 5);
        Assert.Equal(XPathContextKind.ElementName, ctx.Kind);
        Assert.Equal("boo", ctx.Prefix);
    }

    [Fact]
    public void InsidePredicateAtSignIsAttributeName()
    {
        var ctx = XPathContextProvider.DetectContext("//book[@", 8);
        Assert.Equal(XPathContextKind.AttributeName, ctx.Kind);
        Assert.Equal("", ctx.Prefix);
    }

    [Fact]
    public void PartialAttributeName()
    {
        var ctx = XPathContextProvider.DetectContext("//book[@isb", 11);
        Assert.Equal(XPathContextKind.AttributeName, ctx.Kind);
        Assert.Equal("isb", ctx.Prefix);
    }

    [Fact]
    public void StandaloneWordIsFunctionName()
    {
        var ctx = XPathContextProvider.DetectContext("coun", 4);
        Assert.Equal(XPathContextKind.FunctionName, ctx.Kind);
        Assert.Equal("coun", ctx.Prefix);
    }
}
```

- [ ] **Step 2: Implement context detection**

Create `src/Phoenixml.Editor/Find/XPathContextProvider.cs`:

```csharp
using System;
using System.Collections.Generic;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml;

namespace Phoenixml.Editor.Find;

/// <summary>Kind of token the caret is currently positioned to complete.</summary>
public enum XPathContextKind { None, FunctionName, ElementName, AttributeName }

/// <summary>Detected context at the caret: kind + the prefix typed so far.</summary>
public sealed record XPathContext(XPathContextKind Kind, string Prefix);

/// <summary>
/// Caret-aware token-classifier for the XPath query bar. Decides whether the prefix at the
/// caret should be completed against function names, element names, or attribute names.
/// </summary>
/// <remarks>
/// Heuristics:
/// <list type="bullet">
///   <item><description><c>//</c> or <c>/</c> immediately to the left of the word → element name</description></item>
///   <item><description><c>@</c> immediately to the left of the word → attribute name</description></item>
///   <item><description>Otherwise → function name</description></item>
/// </list>
/// </remarks>
public static class XPathContextProvider
{
    /// <summary>Classifies the token at <paramref name="caret"/> in <paramref name="text"/>.</summary>
    public static XPathContext DetectContext(string? text, int caret)
    {
        if (string.IsNullOrEmpty(text) || caret < 0 || caret > text.Length)
            return new XPathContext(XPathContextKind.None, string.Empty);

        // Read backwards through word characters to find the prefix.
        int start = caret;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        var prefix = text.Substring(start, caret - start);

        // Look at the character immediately before the prefix.
        if (start == 0)
            return new XPathContext(
                prefix.Length > 0 ? XPathContextKind.FunctionName : XPathContextKind.None,
                prefix);

        var before = text[start - 1];
        if (before == '@')
            return new XPathContext(XPathContextKind.AttributeName, prefix);
        if (before == '/')
            return new XPathContext(XPathContextKind.ElementName, prefix);

        return new XPathContext(XPathContextKind.FunctionName, prefix);
    }

    /// <summary>Collects distinct element local names from <paramref name="doc"/>.</summary>
    public static IReadOnlyList<string> CollectElementNames(XmlDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var set = new HashSet<string>(StringComparer.Ordinal);
        Walk(doc, doc.Root.Id, set, attributes: false);
        var list = new List<string>(set);
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    /// <summary>Collects distinct attribute local names from <paramref name="doc"/>.</summary>
    public static IReadOnlyList<string> CollectAttributeNames(XmlDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var set = new HashSet<string>(StringComparer.Ordinal);
        Walk(doc, doc.Root.Id, set, attributes: true);
        var list = new List<string>(set);
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static void Walk(XmlDocument doc, PhoenixmlDb.Core.NodeId id, HashSet<string> set, bool attributes)
    {
        var node = doc.Resolve(id);
        if (node is null) return;
        if (node is XdmElement el)
        {
            if (!attributes) set.Add(el.LocalName);
            if (attributes)
            {
                foreach (var attrId in el.Attributes)
                {
                    if (doc.Resolve(attrId) is XdmAttribute attr)
                        set.Add(attr.LocalName);
                }
            }
            foreach (var childId in el.Children)
                Walk(doc, childId, set, attributes);
        }
        else if (node is XdmDocument d)
        {
            foreach (var childId in d.Children)
                Walk(doc, childId, set, attributes);
        }
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ':';
}
```

- [ ] **Step 3: Run context tests**

Run: `dotnet test test/Phoenixml.Editor.Tests/Phoenixml.Editor.Tests.csproj --filter "FullyQualifiedName~XPathContextProviderTests" --nologo 2>&1 | tail -3`
Expected: 6 / 6 passing.

- [ ] **Step 4: Route the bar's popup by context**

In `XPathQueryBar.axaml.cs`, add a new event:

```csharp
    /// <summary>
    /// Requests element / attribute names from the host (typically harvested from the
    /// active document). The host returns the list filtered to <paramref name="prefix"/>.
    /// </summary>
    public event Func<XPathContextKind, string, IReadOnlyList<string>>? NamesRequested;
```

In the `TextChanged` handler that drives the completion popup, replace the body with:

```csharp
        queryBox.TextChanged += (_, _) =>
        {
            var ctx = XPathContextProvider.DetectContext(queryBox.Text, queryBox.CaretIndex);
            if (ctx.Kind == XPathContextKind.None)
            {
                popup.IsOpen = false;
                return;
            }

            IReadOnlyList<string> matches = ctx.Kind switch
            {
                XPathContextKind.FunctionName => XPathFunctionList.Match(ctx.Prefix),
                XPathContextKind.ElementName or XPathContextKind.AttributeName =>
                    NamesRequested?.Invoke(ctx.Kind, ctx.Prefix) ?? [],
                _ => [],
            };

            if (matches.Count == 0)
            {
                popup.IsOpen = false;
                return;
            }
            completionList.ItemsSource = matches;
            completionList.SelectedIndex = 0;
            popup.IsOpen = true;
        };
```

- [ ] **Step 5: Implement the host callback**

In `EditorMainWindow.cs`, wire `_xpathBar.NamesRequested`:

```csharp
        _xpathBar.NamesRequested += (kind, prefix) =>
        {
            if (_state.ActiveDocument is not XmlDocument doc) return [];
            var all = kind switch
            {
                XPathContextKind.ElementName => XPathContextProvider.CollectElementNames(doc),
                XPathContextKind.AttributeName => XPathContextProvider.CollectAttributeNames(doc),
                _ => (IReadOnlyList<string>)[],
            };
            if (prefix.Length == 0) return all;
            var filtered = new List<string>();
            foreach (var n in all)
                if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    filtered.Add(n);
            return filtered;
        };
```

- [ ] **Step 6: Run full suite**

Run: `dotnet test --nologo 2>&1 | tail -5`
Expected: green.

---

### Task 8: Namespace-aware function autocompletion

**Files:**
- Modify: `src/Phoenixml.Editor/Find/XPathFunctionList.cs`
- Create: `test/Phoenixml.Editor.Tests/Find/XPathFunctionListNamespaceTests.cs`

- [ ] **Step 1: Write namespace tests**

Create `test/Phoenixml.Editor.Tests/Find/XPathFunctionListNamespaceTests.cs`:

```csharp
using Phoenixml.Editor.Find;
using Xunit;

namespace Phoenixml.Editor.Tests.Find;

public class XPathFunctionListNamespaceTests
{
    [Fact]
    public void MapPrefixReturnsOnlyMapFunctions()
    {
        var matches = XPathFunctionList.Match("map:");
        Assert.Contains("map:keys", matches);
        Assert.DoesNotContain("count", matches);
    }

    [Fact]
    public void ArrayPrefixReturnsOnlyArrayFunctions()
    {
        var matches = XPathFunctionList.Match("array:");
        Assert.Contains("array:size", matches);
        Assert.DoesNotContain("count", matches);
    }

    [Fact]
    public void XsPrefixReturnsOnlyXsTypes()
    {
        var matches = XPathFunctionList.Match("xs:");
        Assert.Contains("xs:string", matches);
        Assert.DoesNotContain("count", matches);
    }

    [Fact]
    public void NoPrefixReturnsFnFunctionsOnly()
    {
        var matches = XPathFunctionList.Match("coun");
        Assert.Contains("count", matches);
        Assert.DoesNotContain("map:keys", matches);
    }
}
```

- [ ] **Step 2: Extend `XPathFunctionList` with namespace buckets**

In `src/Phoenixml.Editor/Find/XPathFunctionList.cs`, replace the body with:

```csharp
public static class XPathFunctionList
{
    // fn: namespace local names — rendered without prefix.
    private static readonly string[] FnNames =
    [
        "abs", "boolean", "ceiling", "codepoints-to-string", "compare", "concat",
        "contains", "count", "current-date", "current-dateTime", "current-time",
        "data", "deep-equal", "distinct-values", "doc", "doc-available", "empty",
        "ends-with", "error", "exists", "false", "filter", "floor", "fold-left",
        "fold-right", "for-each", "format-date", "format-dateTime", "format-number",
        "format-time", "head", "id", "idref", "implicit-timezone", "index-of",
        "last", "local-name", "lower-case", "matches", "max", "min", "name",
        "namespace-uri", "nilled", "node-name", "normalize-space", "not", "number",
        "parse-xml", "path", "position", "prefix-from-QName", "remove",
        "replace", "resolve-uri", "reverse", "root", "round", "round-half-to-even",
        "serialize", "starts-with", "string", "string-join", "string-length",
        "string-to-codepoints", "subsequence", "substring", "substring-after",
        "substring-before", "sum", "tail", "tokenize", "trace", "translate",
        "true", "unparsed-text", "upper-case", "year-from-date",
        "year-from-dateTime", "zero-or-one",
    ];

    // map: namespace functions.
    private static readonly string[] MapNames =
    [
        "map:contains", "map:entry", "map:find", "map:for-each", "map:get",
        "map:keys", "map:merge", "map:put", "map:remove", "map:size",
    ];

    // array: namespace functions.
    private static readonly string[] ArrayNames =
    [
        "array:append", "array:filter", "array:flatten", "array:fold-left",
        "array:fold-right", "array:for-each", "array:for-each-pair", "array:get",
        "array:head", "array:insert-before", "array:join", "array:put",
        "array:remove", "array:reverse", "array:size", "array:sort", "array:subarray",
        "array:tail",
    ];

    // xs: built-in types relevant in XPath casts and parameter annotations.
    private static readonly string[] XsNames =
    [
        "xs:anyAtomicType", "xs:anyURI", "xs:base64Binary", "xs:boolean", "xs:byte",
        "xs:date", "xs:dateTime", "xs:dayTimeDuration", "xs:decimal", "xs:double",
        "xs:duration", "xs:float", "xs:gDay", "xs:gMonth", "xs:gMonthDay", "xs:gYear",
        "xs:gYearMonth", "xs:hexBinary", "xs:int", "xs:integer", "xs:language",
        "xs:long", "xs:Name", "xs:NCName", "xs:negativeInteger", "xs:nonNegativeInteger",
        "xs:nonPositiveInteger", "xs:normalizedString", "xs:positiveInteger",
        "xs:QName", "xs:short", "xs:string", "xs:time", "xs:token", "xs:unsignedByte",
        "xs:unsignedInt", "xs:unsignedLong", "xs:unsignedShort", "xs:yearMonthDuration",
    ];

    /// <summary>Returns names matching <paramref name="prefix"/>. Routes by namespace prefix.</summary>
    public static IReadOnlyList<string> Match(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        // Namespace-prefixed queries select the matching bucket.
        if (prefix.StartsWith("map:", StringComparison.Ordinal))
            return Filter(MapNames, prefix);
        if (prefix.StartsWith("array:", StringComparison.Ordinal))
            return Filter(ArrayNames, prefix);
        if (prefix.StartsWith("xs:", StringComparison.Ordinal))
            return Filter(XsNames, prefix);
        // Unprefixed → fn: bucket (which renders without prefix).
        return Filter(FnNames, prefix);
    }

    private static IReadOnlyList<string> Filter(string[] source, string prefix)
    {
        if (prefix.Length == 0) return source;
        var list = new System.Collections.Generic.List<string>();
        foreach (var n in source)
        {
            if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                list.Add(n);
        }
        return list;
    }
}
```

- [ ] **Step 3: Run all function-list tests**

Run: `dotnet test test/Phoenixml.Editor.Tests/Phoenixml.Editor.Tests.csproj --filter "FullyQualifiedName~XPathFunctionList" --nologo 2>&1 | tail -3`
Expected: 3 + 4 = 7 tests passing.

---

### Task 9: Walkthrough doc + commit

**Files:**
- Create: `docs/editor-v0.14-walkthrough.md`

- [ ] **Step 1: Write v0.14 walkthrough**

Create `docs/editor-v0.14-walkthrough.md` (template mirrors `v0.13-walkthrough.md`):

```markdown
# Phoenixml.Editor v0.14 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-13:** v0.14 ships the Plan 24 carried-features pack — six items that were deferred from Plan 23. Highlights: dual-pane side-by-side compare with synced scrolling and tinted diff rows, drop preview ghost, persistent XPath history, XPath function signature hints, context-aware XPath autocompletion (element / attribute names from the active doc), and namespace-aware (`map:`, `array:`, `xs:`) autocompletion.

## What's new in v0.14

### Dual-pane compare (Plan 24 #1)

File → Compare With… now opens a side-by-side window with two `NodeListView` instances. Rows are tinted by diff status: green for added, red for removed, yellow for changed. Scrolling either pane scrolls the other to the same vertical offset.

Limitations carried to Plan 25:
- No structural row alignment — each pane shows its document in natural order; matching across panes relies on canonical XPath, not visual row position.
- No keyboard navigation between diff entries.
- No in-line editing.
- No three-way merge.

### Drop preview ghost (Plan 24 #2)

While dragging a tree row, a small floating chip displays `Move <element-name>` at the cursor. Disappears on drop/cancel.

### Persistent XPath history (Plan 24 #3)

XPath query bar history persists across editor restarts. Stored at `~/.config/phoenixml/xpath-history.txt`, newline-delimited. Loaded on startup, saved on window close.

### XPath function signature hints (Plan 24 #4)

When the caret sits inside the parentheses of a recognized function (e.g. `count(`), a popup above the input shows the function signature.

### Context-aware XPath autocompletion (Plan 24 #5)

The completion popup now routes by caret context:
- After `/` or `//` → element names harvested from the active document.
- After `@` (inside a predicate) → attribute names from the active document.
- Otherwise → `fn:` function names.

### Namespace-aware autocompletion (Plan 24 #6)

Typing `map:`, `array:`, or `xs:` switches the popup to the corresponding namespace bucket. Unprefixed input continues to filter `fn:` functions.

## Smoke-test checklist (manual)

| Action | Expected | Actual |
|---|---|---|
| File → Compare With… → pick a doc | Dual-pane window opens; diff rows tinted; counts match Plan 23 textual report. | tbd |
| Scroll the left pane | Right pane scrolls in lockstep. | tbd |
| Drag a tree row | Ghost chip shows `Move <name>` near the cursor; disappears on drop. | tbd |
| Ctrl+G, type `//a`, Enter, close + relaunch editor | After relaunch, Ctrl+G + Up shows `//a`. | tbd |
| Ctrl+G, type `count(`, leave caret after `(` | Signature popup above input shows `count($arg as item()*) as xs:integer`. | tbd |
| Type `//boo` | Completion popup shows element names starting with `boo` from the active doc. | tbd |
| Type `//book[@isb` | Completion popup shows attributes starting with `isb` from the active doc. | tbd |
| Type `map:` | Popup switches to `map:keys`, `map:get`, etc. | tbd |
| Type `xs:` | Popup switches to `xs:string`, `xs:integer`, etc. | tbd |

## Validated environment

- OS: Linux Mint 22.3 (Cinnamon, X11)
- .NET: 10.0
- Avalonia: 11.2.4
- Phoenixml.Platform: 0.14.0 (in-repo)
- Phoenixml.Platform.Editor.Xml: 0.10.0 (in-repo)
- PhoenixmlDb.Core: 1.1.2 (NuGet)
- PhoenixmlDb.XQuery: 1.3.6 (NuGet)
- PhoenixmlDb.Xslt: 1.3.10 (CPM pinned, not referenced)
```

- [ ] **Step 2: Run the full suite**

Run: `dotnet test --nologo 2>&1 | tail -8`
Expected: all green; +20 new tests (4 DiffIndexBuilder, 3 history-store, 3 signatures, 6 context, 4 namespace function-list).

- [ ] **Step 3: Single commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
feat(editor): v0.14 carried-features pack — Plan 24 (6 features)

1. Dual-pane compare — new CompareDocsWindow with two NodeListView
   instances; synced scrolling; rows tinted by diff status via new
   DiffIndex (parallel to MatchIndex). Reuses XmlDocumentDiff from Plan 23.

2. Drop preview ghost — Popup chip showing 'Move <name>' follows the
   cursor during drag.

3. Persistent XPath history — XPathHistoryStore reads/writes
   ~/.config/phoenixml/xpath-history.txt; loaded at startup, saved on close.

4. XPath function signature hints — XPathSignatures map (30+ fn:*
   signatures); popup above input shows signature when caret sits past
   an open paren of a known function.

5. Context-aware autocompletion — XPathContextProvider classifies the
   caret as function name / element name / attribute name. After / or //
   suggests element names from the active doc; after @ suggests attribute
   names. NamesRequested host callback harvests via XdmDocument walk.

6. Namespace-aware function list — map:, array:, xs: prefixes route to
   the corresponding bucket; unprefixed input continues to filter fn:.

20 new unit tests (DiffIndexBuilder x4, history store x3, signatures x3,
context provider x6, namespace function list x4). Full suite green.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Out of scope (deferred to Plan 25)

- Structural row alignment in dual-pane compare (rows in each pane laid out by document order, not by aligned diff lines)
- Keyboard navigation between diffs (next/previous change)
- In-line editing in compare panes
- Three-way merge
- Schema-aware autocompletion (use the active XSD when present)
- Persistence of more than just XPath history (UI state, last-active doc, etc.)
