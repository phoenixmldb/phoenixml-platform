# Editor v0.13 — Plan 23 Polish Pack — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship 8 polish items that round out the v0.12 surface — Copy XPath, failed-action reporting, document compare (MVP), attribute clipboard, drag-drop polish, breadcrumb ellipsization, XPath history, XPath function autocompletion (MVP).

**Architecture:** All items extend existing infrastructure (`NodeListView` row context menu, `ClipboardService`, `StatusBarSegment`, `XPathBuilder`, `BreadcrumbView`, `XPathQueryBar`). MVP-scoped: compare = textual structural diff dumped into Problems-style list (no dual NodeListView yet); autocompletion = popup listing `fn:*` name prefix matches (no parameter hints / context filtering).

**Tech Stack:** Avalonia 11.2.4, PhoenixmlDb.Core/XQuery, existing editor commands.

**Scope discipline:** The MVPs for items 3 and 8 are explicitly minimal so the whole pack fits one execution session. Full dual-pane diff view and semantic autocompletion are deferred to Plans 24/25.

---

## File structure

| File | Role |
|---|---|
| `src/Phoenixml.Platform/Editing/NodeListView.axaml(.cs)` | Modify — add "Copy XPath" menu item; existing RowContextMenu_Opening already stashes rowVm |
| `src/Phoenixml.Editor/EditorMainWindow.cs` | Modify — wire Copy XPath, failed-action segment, attribute clipboard, compare command, breadcrumb ellipsize, history wiring |
| `src/Phoenixml.Editor/Status/LastActionStatus.cs` | NEW — TTL-cleared notification for the new `last-action` status segment |
| `src/Phoenixml.Editor/Clipboard/AttributeClipboardFormat.cs` | NEW — attribute serialize/parse helpers (`name="value"` wire format) |
| `src/Phoenixml.Platform.Editor.Xml/Editing/InsertAttributeFromClipboardCommand.cs` | NEW — paste an attribute onto an element |
| `src/Phoenixml.Platform.Editor.Xml/Diff/XmlDocumentDiff.cs` | NEW — structural diff (added/removed/changed nodes by XPath) |
| `src/Phoenixml.Platform/Editing/NodeListView.axaml.cs` | Modify — drag auto-scroll near viewport edges; drop-ghost adorner |
| `src/Phoenixml.Platform/Editing/BreadcrumbView.axaml.cs` | Modify — width-aware ellipsization (collapse middle to `… ›`) |
| `src/Phoenixml.Editor/Find/XPathQueryBar.axaml(.cs)` | Modify — up/down arrow history cycle, function-name popup |
| `src/Phoenixml.Editor/Find/XPathHistory.cs` | NEW — per-session history (LRU, deduplicated, capped at 50) |
| `src/Phoenixml.Editor/Find/XPathFunctionList.cs` | NEW — static `fn:*` name list, prefix filter |
| `test/Phoenixml.Platform.Editor.Xml.Tests/Diff/XmlDocumentDiffTests.cs` | NEW — 6 diff tests |
| `test/Phoenixml.Platform.Editor.Xml.Tests/Editing/InsertAttributeFromClipboardCommandTests.cs` | NEW — 4 paste-attribute tests |
| `test/Phoenixml.Editor.Tests/Find/XPathHistoryTests.cs` | NEW — 4 history tests |
| `test/Phoenixml.Editor.Tests/Find/XPathFunctionListTests.cs` | NEW — 3 prefix-match tests |
| `test/Phoenixml.Editor.Tests/Clipboard/AttributeClipboardFormatTests.cs` | NEW — 4 roundtrip tests |
| `test/Phoenixml.Editor.Tests/Status/LastActionStatusTests.cs` | NEW — 3 TTL tests |
| `docs/editor-v0.13-walkthrough.md` | NEW — v0.13 walkthrough |

---

## Tasks

### Task 1: Copy XPath on tree rows

**Files:**
- Modify: `src/Phoenixml.Platform/Editing/NodeListView.axaml`
- Modify: `src/Phoenixml.Platform/Editing/NodeListView.axaml.cs`
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs`

- [ ] **Step 1: Add the menu item to NodeListView.axaml**

In `<ContextMenu Opening="RowContextMenu_Opening">`, after the existing `<MenuItem Header="Delete" ...>` and before `</ContextMenu>`, add:

```xml
                            <Separator />
                            <MenuItem Header="Copy XPath"
                                      Click="CopyXPath_Click"
                                      Tag="{Binding}" />
```

- [ ] **Step 2: Add the event + handler to NodeListView.axaml.cs**

Find the events section (where `NodeInsertChildElementRequested` is declared, around line 299). After the existing events, add:

```csharp
    /// <summary>
    /// Raised when the user picks "Copy XPath" from a row's context menu. The host
    /// is expected to build the canonical XPath and write it to the system clipboard.
    /// </summary>
#pragma warning disable CA1003
    public event Action<ITreeNode>? NodeCopyXPathRequested;
#pragma warning restore CA1003
```

Find the click handlers section (where `Delete_Click` lives) and add:

```csharp
    private void CopyXPath_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is NodeRowViewModel row)
            NodeCopyXPathRequested?.Invoke(row.Node);
    }
```

- [ ] **Step 3: Wire from EditorMainWindow**

In `EditorMainWindow.cs`, find the block where other `_nodeList.Node*Requested` handlers are wired (near the `NodeInsertChildElementRequested` hookup around line 134). After those handlers, add:

```csharp
        _nodeList.NodeCopyXPathRequested += async node =>
        {
            if (_clipboard is null) return;
            if (_state.ActiveDocument is not XmlDocument doc) return;
            if (node is not XmlTreeNode xmlNode) return;

            var xpath = XPathBuilder.Build(doc, xmlNode.XdmNode.Id);
            if (string.IsNullOrEmpty(xpath)) return;
            await _clipboard.WriteTextAsync(xpath);
            NotifyAction($"Copied XPath: {xpath}");
        };
```

(`NotifyAction` is the helper added in Task 2; for now the line will not compile — Task 2 introduces it.)

- [ ] **Step 4: Verify build is red on `NotifyAction` and `WriteTextAsync`**

Run: `dotnet build src/Phoenixml.Editor/Phoenixml.Editor.csproj --nologo 2>&1 | grep -E "CS\d{4}" | head -5`
Expected: two errors — `NotifyAction` undefined; `WriteTextAsync` not on `ClipboardService`. Both fixed in Tasks 2 and 4.

---

### Task 2: Failed-action status segment (`LastActionStatus`)

**Files:**
- Create: `src/Phoenixml.Editor/Status/LastActionStatus.cs`
- Create: `test/Phoenixml.Editor.Tests/Status/LastActionStatusTests.cs`
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs`

- [ ] **Step 1: Write `LastActionStatusTests` first**

Create `test/Phoenixml.Editor.Tests/Status/LastActionStatusTests.cs`:

```csharp
using System.Threading.Tasks;
using Phoenixml.Editor.Status;
using Xunit;

namespace Phoenixml.Editor.Tests.Status;

public class LastActionStatusTests
{
    [Fact]
    public void NotifySetsMessageAndRaisesChanged()
    {
        var status = new LastActionStatus(autoClearAfter: TimeSpan.FromMilliseconds(500));
        var changes = 0;
        status.Changed += () => changes++;

        status.Notify("hello");

        Assert.Equal("hello", status.Message);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void NotifyReplacesPriorMessage()
    {
        var status = new LastActionStatus(TimeSpan.FromMilliseconds(500));
        status.Notify("a");
        status.Notify("b");

        Assert.Equal("b", status.Message);
    }

    [Fact]
    public async Task MessageClearsAfterTTL()
    {
        var status = new LastActionStatus(TimeSpan.FromMilliseconds(50));
        var cleared = false;
        status.Changed += () =>
        {
            if (status.Message is null) cleared = true;
        };

        status.Notify("flash");
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.True(cleared, "Message did not clear within TTL window");
        Assert.Null(status.Message);
    }
}
```

- [ ] **Step 2: Run the test — expect a missing-type failure**

Run: `dotnet test test/Phoenixml.Editor.Tests/Phoenixml.Editor.Tests.csproj --filter "FullyQualifiedName~LastActionStatusTests" --nologo 2>&1 | tail -5`
Expected: build fails — `LastActionStatus` not found.

- [ ] **Step 3: Implement `LastActionStatus`**

Create `src/Phoenixml.Editor/Status/LastActionStatus.cs`:

```csharp
using System;
using System.Threading;
using Avalonia.Threading;

namespace Phoenixml.Editor.Status;

/// <summary>
/// Transient status-bar notification with TTL auto-clear. Set by editor commands
/// when they want to surface a silent reject / success message in the
/// <c>last-action</c> status segment without disturbing the rest of the UI.
/// </summary>
/// <remarks>
/// <para>
/// The status auto-clears <see cref="TimeSpan"/> after the most recent <see cref="Notify"/>.
/// Setting a new message while an earlier one is still visible resets the timer.
/// </para>
/// <para>
/// <see cref="Changed"/> fires on both notify and auto-clear; subscribers should re-read
/// <see cref="Message"/> to decide what to render. The auto-clear callback marshals to the
/// UI thread via <see cref="Dispatcher.UIThread"/>.
/// </para>
/// </remarks>
public sealed class LastActionStatus
{
    private readonly TimeSpan _ttl;
    private CancellationTokenSource? _cts;

    public LastActionStatus(TimeSpan autoClearAfter)
    {
        _ttl = autoClearAfter;
    }

    /// <summary>The current message, or <see langword="null"/> when nothing is being shown.</summary>
    public string? Message { get; private set; }

    /// <summary>Fires when <see cref="Message"/> changes (notify or auto-clear).</summary>
    public event Action? Changed;

    /// <summary>Sets <see cref="Message"/> and (re)starts the auto-clear timer.</summary>
    public void Notify(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        Message = message;
        Changed?.Invoke();

        var ct = _cts.Token;
        _ = ScheduleClearAsync(ct);
    }

    private async System.Threading.Tasks.Task ScheduleClearAsync(CancellationToken ct)
    {
        try
        {
            await System.Threading.Tasks.Task.Delay(_ttl, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Message = null;
            Changed?.Invoke();
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                Message = null;
                Changed?.Invoke();
            });
        }
    }
}
```

- [ ] **Step 4: Run the tests — expect green**

Run: `dotnet test test/Phoenixml.Editor.Tests/Phoenixml.Editor.Tests.csproj --filter "FullyQualifiedName~LastActionStatusTests" --nologo 2>&1 | tail -3`
Expected: 3 / 3 passing.

- [ ] **Step 5: Wire `LastActionStatus` into `EditorMainWindow`**

In `EditorMainWindow.cs`, with the other field declarations near the top of the class (after `_xpathScalar`), add:

```csharp
    private readonly LastActionStatus _lastAction = new(TimeSpan.FromSeconds(4));
```

In the constructor, with the other event wiring, add:

```csharp
        _lastAction.Changed += UpdateStatusBar;
```

In `UpdateStatusBar()`, after the existing `selected-xpath` block, add:

```csharp
        if (_lastAction.Message is { } actionMsg)
        {
            segments.Add(new StatusBarSegment(
                Id: "last-action",
                Label: actionMsg,
                Order: 3));
        }
```

Add the `NotifyAction` helper anywhere convenient in the class:

```csharp
    /// <summary>Surfaces a transient message in the status bar's last-action segment.</summary>
    internal void NotifyAction(string message) => _lastAction.Notify(message);
```

- [ ] **Step 6: Surface existing silent rejects via `NotifyAction`**

In `PasteAsync` (around line 893), replace the bare `catch` clauses with messaging:

```csharp
        catch (System.Xml.XmlException ex)
        {
            NotifyAction($"Paste failed: malformed XML ({ex.Message})");
        }
        catch (InvalidOperationException ex)
        {
            NotifyAction($"Paste failed: {ex.Message}");
        }
```

In `CutSelectionAsync`, replace `catch (InvalidOperationException) { ... }` with:

```csharp
        catch (InvalidOperationException ex)
        {
            NotifyAction($"Cut failed: {ex.Message}");
        }
```

In the drop-routing handler (search for `NodeReorderRequested`), wrap the `MoveSubtreeCommand` execute in:

```csharp
        try
        {
            doc.Undo.Execute(cmd);
        }
        catch (InvalidOperationException ex)
        {
            NotifyAction($"Move rejected: {ex.Message}");
        }
```

- [ ] **Step 7: Run full test suite**

Run: `dotnet test --nologo 2>&1 | tail -10`
Expected: all suites pass, including 3 new `LastActionStatus` tests.

---

### Task 3: Compare two documents (MVP — textual diff report)

**Files:**
- Create: `src/Phoenixml.Platform.Editor.Xml/Diff/XmlDocumentDiff.cs`
- Create: `test/Phoenixml.Platform.Editor.Xml.Tests/Diff/XmlDocumentDiffTests.cs`
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs`
- Modify: `src/Phoenixml.Editor/Menus/MenuBuilder.cs` (or wherever menu is built)

**Scope:** No dual NodeListView pane. Compare action computes a structural diff (added / removed / value-changed elements + attributes, keyed by canonical XPath) and dumps the result into a transient modal `Window` with a `TextBlock` summary. Dual-pane UI deferred to Plan 24.

- [ ] **Step 1: Write `XmlDocumentDiffTests` first**

Create `test/Phoenixml.Platform.Editor.Xml.Tests/Diff/XmlDocumentDiffTests.cs`:

```csharp
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml.Diff;
using Xunit;

namespace Phoenixml.Platform.Editor.Xml.Tests.Diff;

public class XmlDocumentDiffTests
{
    private static XmlDocument Parse(string xml)
    {
        var (result, nsTable) = XmlDocument.ParseXml(xml);
        return new XmlDocument(
            Phoenixml.Platform.Abstractions.Documents.DocumentId.New(),
            source: null,
            displayName: "doc.xml",
            result, nsTable);
    }

    [Fact]
    public void IdenticalDocsHaveNoDifferences()
    {
        var a = Parse("<r><x/></r>");
        var b = Parse("<r><x/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        Assert.Empty(diff.Changes);
    }

    [Fact]
    public void AddedElementShowsAsAdded()
    {
        var a = Parse("<r><x/></r>");
        var b = Parse("<r><x/><y/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        var change = Assert.Single(diff.Changes);
        Assert.Equal(DiffKind.Added, change.Kind);
        Assert.Equal("/r/y", change.XPath);
    }

    [Fact]
    public void RemovedElementShowsAsRemoved()
    {
        var a = Parse("<r><x/><y/></r>");
        var b = Parse("<r><x/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        var change = Assert.Single(diff.Changes);
        Assert.Equal(DiffKind.Removed, change.Kind);
        Assert.Equal("/r/y", change.XPath);
    }

    [Fact]
    public void ChangedAttributeShowsAsChanged()
    {
        var a = Parse("<r><x a=\"1\"/></r>");
        var b = Parse("<r><x a=\"2\"/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        var change = Assert.Single(diff.Changes);
        Assert.Equal(DiffKind.Changed, change.Kind);
        Assert.Equal("/r/x/@a", change.XPath);
        Assert.Equal("1", change.LeftValue);
        Assert.Equal("2", change.RightValue);
    }

    [Fact]
    public void ChangedTextShowsAsChanged()
    {
        var a = Parse("<r><x>old</x></r>");
        var b = Parse("<r><x>new</x></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        var change = Assert.Single(diff.Changes);
        Assert.Equal(DiffKind.Changed, change.Kind);
        Assert.Equal("/r/x", change.XPath);
        Assert.Equal("old", change.LeftValue);
        Assert.Equal("new", change.RightValue);
    }

    [Fact]
    public void DiffOrderIsStable()
    {
        var a = Parse("<r><x/><y/></r>");
        var b = Parse("<r><x/><z/></r>");
        var diff = XmlDocumentDiff.Compute(a, b);
        Assert.Equal(2, diff.Changes.Count);
        // Removed before Added when the same parent has both.
        Assert.Equal(DiffKind.Removed, diff.Changes[0].Kind);
        Assert.Equal(DiffKind.Added, diff.Changes[1].Kind);
    }
}
```

- [ ] **Step 2: Implement `XmlDocumentDiff`**

Create `src/Phoenixml.Platform.Editor.Xml/Diff/XmlDocumentDiff.cs`:

```csharp
using System.Collections.Generic;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml.Navigation;

namespace Phoenixml.Platform.Editor.Xml.Diff;

/// <summary>Kind of difference between two documents.</summary>
public enum DiffKind
{
    /// <summary>Node present only in the right (target) document.</summary>
    Added,
    /// <summary>Node present only in the left (source) document.</summary>
    Removed,
    /// <summary>Node value (attribute or text) differs.</summary>
    Changed,
}

/// <summary>
/// A single structural difference between two <see cref="XmlDocument"/>s, keyed by the
/// canonical XPath of the affected node.
/// </summary>
public sealed record DiffChange(
    DiffKind Kind,
    string XPath,
    string? LeftValue,
    string? RightValue);

/// <summary>Result of a structural diff between two documents.</summary>
public sealed record XmlDiffResult(IReadOnlyList<DiffChange> Changes);

/// <summary>
/// Computes a structural diff between two <see cref="XmlDocument"/>s. MVP scope:
/// matches nodes by canonical XPath, reports additions, removals, and attribute/text
/// value changes. Element renames manifest as Removed+Added at the same parent.
/// Order-of-children diffs are not surfaced separately — only set membership.
/// </summary>
public static class XmlDocumentDiff
{
    /// <summary>Computes the diff. The result is independent of the input documents.</summary>
    public static XmlDiffResult Compute(XmlDocument left, XmlDocument right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftMap = BuildMap(left);
        var rightMap = BuildMap(right);

        var changes = new List<DiffChange>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (xpath, leftVal) in leftMap)
        {
            seen.Add(xpath);
            if (rightMap.TryGetValue(xpath, out var rightVal))
            {
                if (!string.Equals(leftVal, rightVal, StringComparison.Ordinal))
                    changes.Add(new DiffChange(DiffKind.Changed, xpath, leftVal, rightVal));
            }
            else
            {
                changes.Add(new DiffChange(DiffKind.Removed, xpath, leftVal, null));
            }
        }

        foreach (var (xpath, rightVal) in rightMap)
        {
            if (seen.Contains(xpath)) continue;
            changes.Add(new DiffChange(DiffKind.Added, xpath, null, rightVal));
        }

        return new XmlDiffResult(changes);
    }

    private static Dictionary<string, string?> BuildMap(XmlDocument doc)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        Walk(doc, doc.Root.Id, map);
        return map;
    }

    private static void Walk(XmlDocument doc, NodeId id, Dictionary<string, string?> map)
    {
        var node = doc.Resolve(id);
        if (node is null) return;

        if (node is XdmElement el)
        {
            var xpath = XPathBuilder.Build(doc, id);
            // Element value: concatenated text content (shallow). Mixed-content elements
            // get the concatenated text of immediate text children; pure-container
            // elements get an empty string. This is enough for MVP diff coverage.
            map[xpath] = ShallowText(doc, el);

            foreach (var attrId in el.Attributes)
            {
                if (doc.Resolve(attrId) is XdmAttribute attr)
                {
                    var attrPath = XPathBuilder.Build(doc, attrId);
                    map[attrPath] = attr.Value;
                }
            }

            foreach (var childId in el.Children)
                Walk(doc, childId, map);
        }
        else if (node is XdmDocument d)
        {
            foreach (var childId in d.Children)
                Walk(doc, childId, map);
        }
    }

    private static string ShallowText(XmlDocument doc, XdmElement el)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var childId in el.Children)
        {
            if (doc.Resolve(childId) is XdmText t)
                sb.Append(t.Value);
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 3: Run diff tests**

Run: `dotnet test test/Phoenixml.Platform.Editor.Xml.Tests/Phoenixml.Platform.Editor.Xml.Tests.csproj --filter "FullyQualifiedName~XmlDocumentDiffTests" --nologo 2>&1 | tail -5`
Expected: 6 / 6 passing.

- [ ] **Step 4: Wire the Compare command**

In `EditorMainWindow.cs`, add a method to handle "File > Compare With…":

```csharp
    /// <summary>
    /// Prompts the user to pick a second XML file, parses it, computes the diff
    /// against the active document, and displays the result in a modal report window.
    /// </summary>
    internal async Task CompareWithFileAsync()
    {
        if (_state.ActiveDocument is not XmlDocument left)
        {
            NotifyAction("Compare: no active document");
            return;
        }

        var topLevel = TopLevel.GetTopLevel(_window);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Compare with…",
                AllowMultiple = false,
            });
        if (files.Count == 0) return;

        XmlDocument right;
        try
        {
            right = await XmlDocument.LoadAsync(files[0].Path.LocalPath, CancellationToken.None);
        }
        catch (System.Xml.XmlException ex)
        {
            NotifyAction($"Compare failed: {ex.Message}");
            return;
        }

        var diff = Diff.XmlDocumentDiff.Compute(left, right);
        ShowDiffReport(left.DisplayName, right.DisplayName, diff);
    }

    private void ShowDiffReport(string leftName, string rightName, Diff.XmlDiffResult diff)
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine($"Compare: {leftName} ↔ {rightName}");
        report.AppendLine($"{diff.Changes.Count} difference(s).");
        report.AppendLine();
        foreach (var c in diff.Changes)
        {
            var verb = c.Kind switch
            {
                Diff.DiffKind.Added => "+",
                Diff.DiffKind.Removed => "−",
                Diff.DiffKind.Changed => "~",
                _ => "?",
            };
            if (c.Kind == Diff.DiffKind.Changed)
                report.AppendLine($"{verb} {c.XPath} : {c.LeftValue} → {c.RightValue}");
            else
                report.AppendLine($"{verb} {c.XPath}");
        }

        var window = new Window
        {
            Title = $"Compare result — {leftName} ↔ {rightName}",
            Width = 700,
            Height = 500,
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = report.ToString(),
                    FontFamily = new Avalonia.Media.FontFamily("Cascadia Mono, Consolas, monospace"),
                    Margin = new Avalonia.Thickness(8),
                    TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
                },
            },
        };
        window.Show(_window);
    }
```

Add the missing `using` if needed: `using PhoenixmlDb.Xdm.Parsing;` (already in scope from existing usings — verify by checking the top of file).

- [ ] **Step 5: Add menu item**

Find the File menu in `EditorMainWindow.cs` / `MenuBuilder.cs`. Look for where "Save" or "Open" menu items are added. Add a "Compare With…" item near them:

```csharp
        var compareItem = new MenuItem { Header = "_Compare With…" };
        compareItem.Click += async (_, _) => await CompareWithFileAsync();
        fileMenu.Items.Add(compareItem);
```

If menu construction lives in a `MenuBuilder` class, follow that file's existing pattern — add the item next to "Open" / "Save".

- [ ] **Step 6: Run tests**

Run: `dotnet build --nologo 2>&1 | tail -5 && dotnet test --nologo --no-build 2>&1 | tail -10`
Expected: build green; all tests still pass.

---

### Task 4: Cut / Copy / Paste attributes

**Files:**
- Create: `src/Phoenixml.Editor/Clipboard/AttributeClipboardFormat.cs`
- Create: `test/Phoenixml.Editor.Tests/Clipboard/AttributeClipboardFormatTests.cs`
- Create: `src/Phoenixml.Platform.Editor.Xml/Editing/InsertAttributeFromClipboardCommand.cs`
- Create: `test/Phoenixml.Platform.Editor.Xml.Tests/Editing/InsertAttributeFromClipboardCommandTests.cs`
- Modify: `src/Phoenixml.Editor/Clipboard/ClipboardService.cs` (add WriteAttributeAsync / ReadAttributeAsync + WriteTextAsync)
- Modify: `src/Phoenixml.Editor/EditorMainWindow.cs` (extend Cut/Copy/Paste to attribute selections)

- [ ] **Step 1: Write the format tests**

Create `test/Phoenixml.Editor.Tests/Clipboard/AttributeClipboardFormatTests.cs`:

```csharp
using Phoenixml.Editor.Clipboard;
using Xunit;

namespace Phoenixml.Editor.Tests.Clipboard;

public class AttributeClipboardFormatTests
{
    [Fact]
    public void SerializeProducesNameEqualsQuotedValue()
    {
        var s = AttributeClipboardFormat.Serialize("isbn", "978-0-13");
        Assert.Equal("isbn=\"978-0-13\"", s);
    }

    [Fact]
    public void SerializeEscapesQuotesAndAmpersands()
    {
        var s = AttributeClipboardFormat.Serialize("title", "He said \"hi\" & wave");
        Assert.Equal("title=\"He said &quot;hi&quot; &amp; wave\"", s);
    }

    [Fact]
    public void ParseRoundTrips()
    {
        var s = AttributeClipboardFormat.Serialize("k", "v\"x");
        var parsed = AttributeClipboardFormat.TryParse(s, out var name, out var value);
        Assert.True(parsed);
        Assert.Equal("k", name);
        Assert.Equal("v\"x", value);
    }

    [Fact]
    public void ParseRejectsMalformed()
    {
        Assert.False(AttributeClipboardFormat.TryParse("not an attribute", out _, out _));
        Assert.False(AttributeClipboardFormat.TryParse("=\"v\"", out _, out _));
        Assert.False(AttributeClipboardFormat.TryParse("k=v", out _, out _));
    }
}
```

- [ ] **Step 2: Implement `AttributeClipboardFormat`**

Create `src/Phoenixml.Editor/Clipboard/AttributeClipboardFormat.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Phoenixml.Editor.Clipboard;

/// <summary>
/// Serialization shim for attribute-clipboard wire format: <c>name="value"</c> with the
/// value entity-escaped (<c>&quot;</c>, <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>).
/// The format is plain-text-clipboard friendly so users can paste into editors that
/// don't understand XML.
/// </summary>
public static class AttributeClipboardFormat
{
    private static readonly Regex Parser = new(
        "^(?<name>[^\\s=\"]+)=\"(?<value>[^\"]*)\"$",
        RegexOptions.Compiled);

    public static string Serialize(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        var escaped = value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
        return $"{name}=\"{escaped}\"";
    }

    public static bool TryParse(string text, out string name, out string value)
    {
        name = string.Empty;
        value = string.Empty;
        if (string.IsNullOrEmpty(text)) return false;
        var m = Parser.Match(text);
        if (!m.Success) return false;
        name = m.Groups["name"].Value;
        var raw = m.Groups["value"].Value;
        value = raw
            .Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal);
        return true;
    }
}
```

- [ ] **Step 3: Extend `ClipboardService` with text + attribute helpers**

In `src/Phoenixml.Editor/Clipboard/ClipboardService.cs`, after the existing `WriteXmlAsync` / `ReadXmlAsync` methods, add:

```csharp
    /// <summary>Writes plain text to the system clipboard.</summary>
    internal async Task WriteTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        await _backend.SetTextAsync(text);
    }

    /// <summary>Reads plain text from the system clipboard, or <see langword="null"/> when empty.</summary>
    internal async Task<string?> ReadTextAsync() => await _backend.GetTextAsync();
```

Verify `IClipboardBackend` already exposes `SetTextAsync` / `GetTextAsync`; if not, add them to the interface and to the Avalonia adapter (it should — `WriteXmlAsync` likely already goes through these).

- [ ] **Step 4: Run format tests**

Run: `dotnet test test/Phoenixml.Editor.Tests/Phoenixml.Editor.Tests.csproj --filter "FullyQualifiedName~AttributeClipboardFormatTests" --nologo 2>&1 | tail -3`
Expected: 4 / 4 passing.

- [ ] **Step 5: Write `InsertAttributeFromClipboardCommand` tests**

Create `test/Phoenixml.Platform.Editor.Xml.Tests/Editing/InsertAttributeFromClipboardCommandTests.cs`:

```csharp
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Editor.Xml.Editing;
using Xunit;

namespace Phoenixml.Platform.Editor.Xml.Tests.Editing;

public class InsertAttributeFromClipboardCommandTests
{
    private static XmlDocument Parse(string xml)
    {
        var (result, nsTable) = XmlDocument.ParseXml(xml);
        return new XmlDocument(
            Phoenixml.Platform.Abstractions.Documents.DocumentId.New(),
            source: null,
            displayName: "doc.xml",
            result, nsTable);
    }

    [Fact]
    public void AddsAttributeToElement()
    {
        var doc = Parse("<r><x/></r>");
        var xId = ((XdmElement)((XdmElement)doc.Root.DocumentElement!).Children[0]).Id;

        var cmd = new InsertAttributeFromClipboardCommand(doc, xId, "isbn", "978-0-13");
        doc.Undo.Execute(cmd);

        var x = (XdmElement)doc.Resolve(xId)!;
        var attrId = Assert.Single(x.Attributes);
        var attr = (XdmAttribute)doc.Resolve(attrId)!;
        Assert.Equal("isbn", attr.LocalName);
        Assert.Equal("978-0-13", attr.Value);
    }

    [Fact]
    public void UndoRemovesAttribute()
    {
        var doc = Parse("<r><x/></r>");
        var xId = ((XdmElement)((XdmElement)doc.Root.DocumentElement!).Children[0]).Id;
        var cmd = new InsertAttributeFromClipboardCommand(doc, xId, "k", "v");
        doc.Undo.Execute(cmd);

        doc.Undo.Undo();

        var x = (XdmElement)doc.Resolve(xId)!;
        Assert.Empty(x.Attributes);
    }

    [Fact]
    public void ReplacesExistingAttributeOfSameName()
    {
        var doc = Parse("<r><x a=\"old\"/></r>");
        var xId = ((XdmElement)((XdmElement)doc.Root.DocumentElement!).Children[0]).Id;
        var cmd = new InsertAttributeFromClipboardCommand(doc, xId, "a", "new");
        doc.Undo.Execute(cmd);

        var x = (XdmElement)doc.Resolve(xId)!;
        var attr = (XdmAttribute)doc.Resolve(x.Attributes[0])!;
        Assert.Equal("new", attr.Value);
    }

    [Fact]
    public void RejectsInvalidName()
    {
        var doc = Parse("<r><x/></r>");
        var xId = ((XdmElement)((XdmElement)doc.Root.DocumentElement!).Children[0]).Id;
        Assert.Throws<InvalidOperationException>(() =>
            doc.Undo.Execute(new InsertAttributeFromClipboardCommand(doc, xId, "bad name", "v")));
    }
}
```

- [ ] **Step 6: Implement `InsertAttributeFromClipboardCommand`**

Look at an existing command (e.g. `SetAttributeValueCommand`) for the file shape — IEditorCommand surface, ReferencedNodeIds, Apply/Revert.

Create `src/Phoenixml.Platform.Editor.Xml/Editing/InsertAttributeFromClipboardCommand.cs`:

```csharp
using System.Collections.Generic;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Abstractions.Editing;

namespace Phoenixml.Platform.Editor.Xml.Editing;

/// <summary>
/// Adds an attribute to a target element using a name/value pair parsed from the
/// system clipboard. When the element already carries an attribute of the same name,
/// the value is replaced (matching XML semantics — attribute names are unique per element).
/// </summary>
public sealed class InsertAttributeFromClipboardCommand : IEditorCommand
{
    private readonly XmlDocument _doc;
    private readonly NodeId _elementId;
    private readonly string _name;
    private readonly string _value;
    private NodeId? _newAttrId;
    private NodeId? _replacedAttrId;
    private XdmAttribute? _replacedSnapshot;

    public InsertAttributeFromClipboardCommand(XmlDocument doc, NodeId elementId, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        _doc = doc;
        _elementId = elementId;
        _name = name;
        _value = value;
    }

    public string Description => $"Paste attribute @{_name}";

    public IEnumerable<NodeId> ReferencedNodeIds
    {
        get
        {
            yield return _elementId;
            if (_newAttrId is { } n) yield return n;
            if (_replacedAttrId is { } r) yield return r;
        }
    }

    public void Apply()
    {
        if (!XmlConvert.IsStartNCNameChar(_name[0]) ||
            !System.Linq.Enumerable.All(_name, c => XmlConvert.IsNCNameChar(c)))
        {
            throw new InvalidOperationException($"'{_name}' is not a valid XML attribute name.");
        }

        if (_doc.Resolve(_elementId) is not XdmElement el)
            throw new InvalidOperationException($"Element {_elementId} not found.");

        // Replace-if-exists: snapshot the existing attribute, swap in a new XdmAttribute
        // with the same id so element.Attributes ordering is preserved.
        foreach (var existingId in el.Attributes)
        {
            if (_doc.Resolve(existingId) is XdmAttribute existing && existing.LocalName == _name)
            {
                _replacedAttrId = existingId;
                _replacedSnapshot = existing;
                var replacement = new XdmAttribute
                {
                    Id = existingId,
                    LocalName = _name,
                    Namespace = existing.Namespace,
                    Prefix = existing.Prefix,
                    Value = _value,
                    Parent = el.Id,
                };
                _doc.ReplaceNode(existingId, replacement);
                _doc.RaiseChanged(new DocumentChange.Edited(existingId));
                return;
            }
        }

        // New attribute
        var newId = _doc.AllocateNodeId();
        _newAttrId = newId;
        var attr = new XdmAttribute
        {
            Id = newId,
            LocalName = _name,
            Namespace = NamespaceId.None,
            Prefix = null,
            Value = _value,
            Parent = el.Id,
        };
        _doc.AddNode(attr);
        var newAttrs = new System.Collections.Generic.List<NodeId>(el.Attributes) { newId };
        _doc.ReplaceNode(el.Id, el with { Attributes = newAttrs });
        _doc.RaiseChanged(new DocumentChange.Edited(el.Id));
    }

    public void Revert()
    {
        if (_doc.Resolve(_elementId) is not XdmElement el) return;

        if (_replacedSnapshot is { } snap && _replacedAttrId is { } repId)
        {
            _doc.ReplaceNode(repId, snap);
            _doc.RaiseChanged(new DocumentChange.Edited(repId));
            _replacedSnapshot = null;
            _replacedAttrId = null;
            return;
        }

        if (_newAttrId is { } newId)
        {
            var newAttrs = new System.Collections.Generic.List<NodeId>(el.Attributes);
            newAttrs.Remove(newId);
            _doc.ReplaceNode(el.Id, el with { Attributes = newAttrs });
            _doc.RemoveNode(newId);
            _doc.RaiseChanged(new DocumentChange.Edited(el.Id));
            _newAttrId = null;
        }
    }
}
```

Note: the command relies on `_doc.RaiseChanged`, `_doc.AddNode`, `_doc.RemoveNode`, `_doc.ReplaceNode` and `XdmElement.with` — verify these exist by reading an existing command (e.g. `SetAttributeValueCommand.cs`). Adjust signatures to match what the codebase exposes; the test will fail loudly if any names are off.

- [ ] **Step 7: Run command tests**

Run: `dotnet test test/Phoenixml.Platform.Editor.Xml.Tests/Phoenixml.Platform.Editor.Xml.Tests.csproj --filter "FullyQualifiedName~InsertAttributeFromClipboardCommandTests" --nologo 2>&1 | tail -5`
Expected: 4 / 4 passing.

- [ ] **Step 8: Wire Cut/Copy/Paste-attribute in EditorMainWindow**

In `CopySelectionAsync` (around line 844), update the routing — selection may now be an attribute:

```csharp
    private async Task CopySelectionAsync()
    {
        if (_clipboard is null) return;
        if (_state.ActiveDocument is not XmlDocument doc) return;
        if (_nodeList.SelectedNode is not XmlTreeNode xmlNode) return;

        switch (xmlNode.XdmNode)
        {
            case XdmElement el:
                {
                    var xml = SubtreeSerializer.Serialize(doc, el.Id);
                    await _clipboard.WriteXmlAsync(xml);
                    NotifyAction($"Copied element <{el.LocalName}>");
                    return;
                }
            case XdmAttribute attr:
                {
                    var s = AttributeClipboardFormat.Serialize(attr.LocalName, attr.Value);
                    await _clipboard.WriteTextAsync(s);
                    NotifyAction($"Copied attribute @{attr.LocalName}");
                    return;
                }
        }
    }
```

Similar updates for `CutSelectionAsync` (delete attribute via `DeleteAttributeCommand` if one exists, else fall back to a no-op for attribute Cut and surface via `NotifyAction`) and `PasteAsync` (try `AttributeClipboardFormat.TryParse` first; on success route to `InsertAttributeFromClipboardCommand` when the selection is an element; else fall back to existing XML paste).

For `PasteAsync`, replace the body with:

```csharp
    private async Task PasteAsync()
    {
        if (_clipboard is null) return;
        if (_state.ActiveDocument is not XmlDocument doc) return;
        if (_nodeList.SelectedNode is not XmlTreeNode xmlNode) return;

        var text = await _clipboard.ReadTextAsync();
        if (!string.IsNullOrEmpty(text)
            && AttributeClipboardFormat.TryParse(text, out var attrName, out var attrValue))
        {
            if (xmlNode.XdmNode is not XdmElement el)
            {
                NotifyAction("Paste attribute: select an element first");
                return;
            }
            try
            {
                doc.Undo.Execute(new InsertAttributeFromClipboardCommand(doc, el.Id, attrName, attrValue));
                NotifyAction($"Pasted attribute @{attrName}");
            }
            catch (InvalidOperationException ex)
            {
                NotifyAction($"Paste failed: {ex.Message}");
            }
            return;
        }

        var xml = await _clipboard.ReadXmlAsync();
        if (string.IsNullOrEmpty(xml)) return;
        if (xmlNode.XdmNode is not XdmElement parent)
        {
            NotifyAction("Paste subtree: select an element first");
            return;
        }
        try
        {
            doc.Undo.Execute(new InsertSubtreeCommand(doc, parent.Id, xml));
            _nodeList.ExpandById(xmlNode.Id);
            NotifyAction("Pasted subtree");
        }
        catch (System.Xml.XmlException ex)
        {
            NotifyAction($"Paste failed: malformed XML ({ex.Message})");
        }
        catch (InvalidOperationException ex)
        {
            NotifyAction($"Paste failed: {ex.Message}");
        }
    }
```

- [ ] **Step 9: Run full test suite**

Run: `dotnet test --nologo 2>&1 | tail -10`
Expected: all suites pass; +11 tests (3 LastActionStatus, 6 diff, 4 attribute format, 4 attribute command, minus 1 if you removed the existing PasteAsync-replaces-test that no longer applies).

---

### Task 5: Drag auto-scroll + drop ghost label

**Files:**
- Modify: `src/Phoenixml.Platform/Editing/NodeListView.axaml.cs`

- [ ] **Step 1: Auto-scroll near viewport edges**

In `NodeListView.axaml.cs`, find the DragOver handler. Inside it, before the existing drop-indicator logic, add:

```csharp
        // Auto-scroll when the cursor is within EdgeScrollZone of the top or bottom
        // of the visible viewport. Speed is a fixed 1 row per DragOver tick — Avalonia
        // fires DragOver ~16ms while the pointer is over the control.
        if (_rowsHost?.Presenter is { } presenter && _rowsHost.Scroll is { } scroll)
        {
            var pos = e.GetPosition(_rowsHost);
            var height = _rowsHost.Bounds.Height;
            const double EdgeScrollZone = 24;
            const double StepDip = 18; // ~1 row
            if (pos.Y < EdgeScrollZone)
                scroll.Offset = scroll.Offset.WithY(Math.Max(0, scroll.Offset.Y - StepDip));
            else if (pos.Y > height - EdgeScrollZone)
                scroll.Offset = scroll.Offset.WithY(scroll.Offset.Y + StepDip);
        }
```

- [ ] **Step 2: Drop ghost label adorner**

In the `PointerPressed` → drag-start path, after `DragDrop.DoDragDrop(...)` is invoked, set the drag-source row's `ToolTip.Tip` to the dragged element's label, so a small chip follows the cursor. (Avalonia's default drag visual is a generic cursor — adding a `Border + TextBlock` as a transient `Window` that follows the cursor is non-trivial. For the MVP we lean on the tooltip surface and revisit a real adorner later.)

```csharp
        // Show a small tooltip-style hint for the dragged element near the cursor.
        // Stored on the drag source row's properties; cleared in OnDragCompleted.
        if (sourceRow.DataContext is NodeRowViewModel vm)
            ToolTip.SetTip(sourceRow, $"Move <{vm.Label}>");
```

In the drag-end / drop handler, clear the tip:

```csharp
        ToolTip.SetTip(sourceRow, null);
```

- [ ] **Step 3: Build + smoke**

Run: `dotnet build src/Phoenixml.Platform/Phoenixml.Platform.csproj --nologo 2>&1 | tail -5`
Expected: 0 errors.

Note: no unit tests for this task — it's UI-only and requires a manual smoke pass (Task 9's walkthrough table).

- [ ] **Step 4: Run full test suite (no regressions)**

Run: `dotnet test --nologo 2>&1 | tail -5`
Expected: all green.

---

### Task 6: Breadcrumb ellipsization

**Files:**
- Modify: `src/Phoenixml.Platform/Editing/BreadcrumbView.axaml.cs`

- [ ] **Step 1: Read existing layout**

Open `BreadcrumbView.axaml.cs` and find the segment-rendering code (likely a `Render` or `SetSegments` method that appends `Button`s to a `StackPanel`).

- [ ] **Step 2: Add ellipsization helper**

When segment count exceeds 6 (configurable threshold), keep the first 2 and the last 3 segments and render `… ›` in the middle as a non-clickable label. Add the helper just before the existing segment-rendering loop:

```csharp
    private const int MaxVisibleSegments = 6;
    private const int HeadKeep = 2;
    private const int TailKeep = 3;

    private static IReadOnlyList<BreadcrumbSegment> ElidedSegments(IReadOnlyList<BreadcrumbSegment> all)
    {
        if (all.Count <= MaxVisibleSegments) return all;
        var elided = new List<BreadcrumbSegment>(HeadKeep + 1 + TailKeep);
        for (int i = 0; i < HeadKeep; i++) elided.Add(all[i]);
        elided.Add(new BreadcrumbSegment(Label: "…", Payload: null, IsClickable: false));
        for (int i = all.Count - TailKeep; i < all.Count; i++) elided.Add(all[i]);
        return elided;
    }
```

- [ ] **Step 3: Route segment list through the helper**

In the public `SetSegments` (or equivalent) method, before iterating segments to build `Button`s, wrap the input:

```csharp
    public void SetSegments(IReadOnlyList<BreadcrumbSegment> segments)
    {
        var visible = ElidedSegments(segments);
        // ... existing rendering loop iterates `visible` instead of `segments`.
    }
```

In the per-segment loop, when `seg.IsClickable == false`, render a `TextBlock` instead of a `Button`:

```csharp
        if (!seg.IsClickable)
        {
            _host.Children.Add(new TextBlock
            {
                Text = seg.Label,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#9D9D9D")),
                Margin = new Thickness(2, 0),
            });
            continue;
        }
        // existing Button construction...
```

Verify `BreadcrumbSegment` has an `IsClickable` field; if not, add it (default `true`). If the record currently is `BreadcrumbSegment(string Label, object? Payload)`, change to `BreadcrumbSegment(string Label, object? Payload, bool IsClickable = true)` and update call sites only if the analyzer complains (positional records with defaults at the end are fine).

- [ ] **Step 4: Build + visual verify in Task 9 smoke**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: 0 errors. No unit tests for this (UI-only behavioral change); covered by Task 9's manual smoke.

---

### Task 7: XPath history dropdown

**Files:**
- Create: `src/Phoenixml.Editor/Find/XPathHistory.cs`
- Create: `test/Phoenixml.Editor.Tests/Find/XPathHistoryTests.cs`
- Modify: `src/Phoenixml.Editor/Find/XPathQueryBar.axaml.cs`
- Modify: `src/Phoenixml.Editor/Find/XPathQueryController.cs`

- [ ] **Step 1: Write history tests**

Create `test/Phoenixml.Editor.Tests/Find/XPathHistoryTests.cs`:

```csharp
using Phoenixml.Editor.Find;
using Xunit;

namespace Phoenixml.Editor.Tests.Find;

public class XPathHistoryTests
{
    [Fact]
    public void RecordAppendsToFront()
    {
        var h = new XPathHistory(capacity: 5);
        h.Record("//a");
        h.Record("//b");
        Assert.Equal(new[] { "//b", "//a" }, h.Entries);
    }

    [Fact]
    public void DuplicatesAreDeduplicatedToFront()
    {
        var h = new XPathHistory(capacity: 5);
        h.Record("//a");
        h.Record("//b");
        h.Record("//a");
        Assert.Equal(new[] { "//a", "//b" }, h.Entries);
    }

    [Fact]
    public void CapacityCapsTheList()
    {
        var h = new XPathHistory(capacity: 2);
        h.Record("//a");
        h.Record("//b");
        h.Record("//c");
        Assert.Equal(new[] { "//c", "//b" }, h.Entries);
    }

    [Fact]
    public void EmptyAndWhitespaceAreIgnored()
    {
        var h = new XPathHistory(capacity: 5);
        h.Record("");
        h.Record("   ");
        Assert.Empty(h.Entries);
    }
}
```

- [ ] **Step 2: Implement `XPathHistory`**

Create `src/Phoenixml.Editor/Find/XPathHistory.cs`:

```csharp
using System.Collections.Generic;

namespace Phoenixml.Editor.Find;

/// <summary>
/// Per-session XPath query history. Most-recent first, deduplicated (re-submitting
/// an entry moves it to the front), capped at <see cref="Capacity"/>. Not persisted
/// across sessions in v0.13.
/// </summary>
public sealed class XPathHistory
{
    private readonly List<string> _entries = new();

    public XPathHistory(int capacity = 50)
    {
        Capacity = capacity;
    }

    public int Capacity { get; }

    public IReadOnlyList<string> Entries => _entries;

    /// <summary>Appends to the front; dedupes; trims to capacity. Empty/whitespace is ignored.</summary>
    public void Record(string xpath)
    {
        ArgumentNullException.ThrowIfNull(xpath);
        if (string.IsNullOrWhiteSpace(xpath)) return;
        _entries.Remove(xpath);
        _entries.Insert(0, xpath);
        while (_entries.Count > Capacity)
            _entries.RemoveAt(_entries.Count - 1);
    }
}
```

- [ ] **Step 3: Run history tests**

Run: `dotnet test test/Phoenixml.Editor.Tests/Phoenixml.Editor.Tests.csproj --filter "FullyQualifiedName~XPathHistoryTests" --nologo 2>&1 | tail -3`
Expected: 4 / 4 passing.

- [ ] **Step 4: Plumb history through controller + bar**

In `XPathQueryController.cs`, add a field and record on submit:

```csharp
    private readonly XPathHistory _history = new();

    public IReadOnlyList<string> History => _history.Entries;
```

In `OnQuerySubmittedAsync`, after `_lastQuery = xpath;`, add:

```csharp
        _history.Record(xpath);
```

Expose history on `IXPathQueryHost` so the bar can read it back:

```csharp
    /// <summary>Returns the recent-XPath history, most-recent first.</summary>
    IReadOnlyList<string> XPathHistoryEntries { get; }
```

Add to the controller's host wire-up (Task 5 of Plan 21):
- `EditorMainWindow` exposes the history via a property on the bar setter.

For simplicity, add a method on the controller that the bar can call via a callback hooked at wire time:

```csharp
    // In XPathQueryController:
    public IReadOnlyList<string> SnapshotHistory() => _history.Entries.ToArray();
```

- [ ] **Step 5: Up/Down arrow cycle in `XPathQueryBar`**

In `XPathQueryBar.axaml.cs`, add an event:

```csharp
#pragma warning disable CA1003
    /// <summary>Raised when the bar requests the latest history snapshot.</summary>
    public event Func<IReadOnlyList<string>>? HistoryRequested;
#pragma warning restore CA1003
```

In the constructor (where `queryBox.KeyDown` is wired), extend the handler to cycle through history:

```csharp
        int historyCursor = -1;
        queryBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Up)
            {
                var hist = HistoryRequested?.Invoke();
                if (hist is null || hist.Count == 0) return;
                historyCursor = Math.Min(historyCursor + 1, hist.Count - 1);
                queryBox.Text = hist[historyCursor];
                queryBox.CaretIndex = queryBox.Text.Length;
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                var hist = HistoryRequested?.Invoke();
                if (hist is null || hist.Count == 0) return;
                historyCursor = Math.Max(historyCursor - 1, -1);
                queryBox.Text = historyCursor == -1 ? string.Empty : hist[historyCursor];
                queryBox.CaretIndex = queryBox.Text.Length;
                e.Handled = true;
            }
            // ... existing Enter / Escape / etc.
        };
```

- [ ] **Step 6: Wire the callback in EditorMainWindow**

After the existing `_xpathBar.QuerySubmitted += ...` lines, add:

```csharp
        _xpathBar.HistoryRequested += () => _xpathController.SnapshotHistory();
```

- [ ] **Step 7: Run full suite**

Run: `dotnet test --nologo 2>&1 | tail -5`
Expected: green, including 4 new history tests.

---

### Task 8: XPath function autocompletion (MVP — fn:* prefix popup)

**Files:**
- Create: `src/Phoenixml.Editor/Find/XPathFunctionList.cs`
- Create: `test/Phoenixml.Editor.Tests/Find/XPathFunctionListTests.cs`
- Modify: `src/Phoenixml.Editor/Find/XPathQueryBar.axaml`
- Modify: `src/Phoenixml.Editor/Find/XPathQueryBar.axaml.cs`

**Scope:** No parameter hints, no schema-aware filtering. Just a popup `ListBox` listing `fn:` function names whose local name starts with the prefix at the caret. Up/Down navigate, Tab accepts.

- [ ] **Step 1: Write function-list tests**

Create `test/Phoenixml.Editor.Tests/Find/XPathFunctionListTests.cs`:

```csharp
using System.Linq;
using Phoenixml.Editor.Find;
using Xunit;

namespace Phoenixml.Editor.Tests.Find;

public class XPathFunctionListTests
{
    [Fact]
    public void ReturnsAllOnEmptyPrefix()
    {
        var matches = XPathFunctionList.Match("");
        Assert.NotEmpty(matches);
    }

    [Fact]
    public void FiltersByPrefix()
    {
        var matches = XPathFunctionList.Match("coun");
        Assert.Contains("count", matches);
        Assert.DoesNotContain("position", matches);
    }

    [Fact]
    public void MatchIsCaseInsensitive()
    {
        var matches = XPathFunctionList.Match("STR");
        Assert.Contains("string", matches);
        Assert.Contains("string-length", matches);
    }
}
```

- [ ] **Step 2: Implement `XPathFunctionList`**

Create `src/Phoenixml.Editor/Find/XPathFunctionList.cs` with a curated list of the most common XPath 3.1 `fn:` functions:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Phoenixml.Editor.Find;

/// <summary>
/// MVP autocompletion source for the XPath query bar. Hand-curated list of common
/// XPath 3.1 <c>fn:*</c> function names (local names only). Filtered by case-insensitive
/// prefix match. v0.13 has no signature / parameter / namespace-aware filtering —
/// those are Plan 24 candidates.
/// </summary>
public static class XPathFunctionList
{
    private static readonly string[] Names =
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

    /// <summary>Returns names whose local name starts with <paramref name="prefix"/>, case-insensitive. Empty prefix returns all.</summary>
    public static IReadOnlyList<string> Match(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Length == 0) return Names;
        return Names
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
```

- [ ] **Step 3: Run function-list tests**

Run: `dotnet test test/Phoenixml.Editor.Tests/Phoenixml.Editor.Tests.csproj --filter "FullyQualifiedName~XPathFunctionListTests" --nologo 2>&1 | tail -3`
Expected: 3 / 3 passing.

- [ ] **Step 4: Add the popup to `XPathQueryBar.axaml`**

In `XPathQueryBar.axaml`, after the existing `<TextBox Name="QueryBox" .../>`, wrap the input + popup in a `Panel`:

```xml
        <Panel>
            <TextBox Name="QueryBox" Width="320"
                     Watermark="//book[year='1978']"
                     VerticalAlignment="Center"
                     FontFamily="Cascadia Mono, Consolas, monospace" />
            <Popup Name="CompletionPopup"
                   PlacementTarget="{Binding ElementName=QueryBox}"
                   Placement="BottomEdgeAlignedLeft"
                   IsLightDismissEnabled="True">
                <Border Background="#252526" BorderBrush="#4A4A4F" BorderThickness="1" Padding="4">
                    <ListBox Name="CompletionList"
                             MaxHeight="240" Width="220"
                             Background="Transparent"
                             FontFamily="Cascadia Mono, Consolas, monospace" />
                </Border>
            </Popup>
        </Panel>
```

- [ ] **Step 5: Wire the popup behaviour in `XPathQueryBar.axaml.cs`**

Inside the existing `InitializeComponent` after the existing `queryBox.KeyDown` block, add:

```csharp
        var popup = this.FindControl<Popup>("CompletionPopup")!;
        var list = this.FindControl<ListBox>("CompletionList")!;

        // Trigger: caret is inside a contiguous word run of [A-Za-z-]+. Update popup as
        // the user types; light-dismiss on focus loss or Escape.
        queryBox.TextChanged += (_, _) =>
        {
            var prefix = WordPrefixAtCaret(queryBox.Text, queryBox.CaretIndex);
            if (prefix is null || prefix.Length == 0)
            {
                popup.IsOpen = false;
                return;
            }
            var matches = XPathFunctionList.Match(prefix);
            if (matches.Count == 0)
            {
                popup.IsOpen = false;
                return;
            }
            list.ItemsSource = matches;
            list.SelectedIndex = 0;
            popup.IsOpen = true;
        };

        queryBox.KeyDown += (_, e) =>
        {
            if (!popup.IsOpen) return;

            if (e.Key == Key.Tab)
            {
                if (list.SelectedItem is string pick)
                    ReplacePrefixAtCaret(queryBox, pick);
                popup.IsOpen = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Up && list.Items.Count > 0)
            {
                list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Down && list.Items.Count > 0)
            {
                list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                popup.IsOpen = false;
                // Don't handle — let the bar's Escape close logic still fire if popup closed.
            }
        };
```

Add the helpers at the bottom of the class:

```csharp
    private static string? WordPrefixAtCaret(string? text, int caret)
    {
        if (string.IsNullOrEmpty(text) || caret <= 0) return null;
        int start = caret;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        if (start == caret) return null;
        return text.Substring(start, caret - start);
    }

    private static void ReplacePrefixAtCaret(TextBox tb, string replacement)
    {
        var text = tb.Text ?? string.Empty;
        var caret = tb.CaretIndex;
        int start = caret;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        var newText = text.Substring(0, start) + replacement + text.Substring(caret);
        tb.Text = newText;
        tb.CaretIndex = start + replacement.Length;
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '-' || c == '_';
```

**Note:** The Up/Down arrows here override the history-cycle behaviour from Task 7 when the popup is open. That's the desired UX — popup takes priority over history navigation. History cycling resumes when the popup closes.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test --nologo 2>&1 | tail -5`
Expected: all suites pass; +7 tests (4 history, 3 function list).

---

### Task 9: Walkthrough doc + final commit

**Files:**
- Create: `docs/editor-v0.13-walkthrough.md`

- [ ] **Step 1: Write the v0.13 walkthrough**

Create `docs/editor-v0.13-walkthrough.md`:

````markdown
# Phoenixml.Editor v0.13 — Walkthrough & Rough-Edges Log

**Status as of 2026-05-13:** v0.13 ships the "Plan 23 polish pack" — eight quality-of-life improvements stacking on top of v0.12. Highlights: Copy XPath context-menu, `last-action` status segment surfacing previously-silent rejects, MVP file-compare command, attribute cut/copy/paste, drag auto-scroll, breadcrumb ellipsization, XPath history (Up/Down), and XPath `fn:*` autocompletion popup (Tab to accept).

## How to run

```bash
cd /raid/elvogel/repos/phoenixml-platform
dotnet run --project src/Phoenixml.Editor --configuration Release -- \
  /raid/tmp/v0.8-smoke/data-and-schemas-by-type/data/library.xml
```

## What's new in v0.13

### Copy XPath (Plan 23 #1)

- Right-click any tree row → **Copy XPath**. The canonical XPath
  (e.g. `/library/book[2]/title`) lands on the system clipboard.
  `last-action` status segment confirms the copy.

### Failed-action status segment (Plan 23 #2)

- New `last-action` status segment surfaces previously-silent rejects:
  - Cycle-rejected drag-drops
  - Malformed-XML pastes
  - Unsupported paste targets
  - Cut/Copy/Paste action confirmations
- Auto-clears 4 seconds after the most recent notification.
- `LastActionStatus` is a generic notification primitive — future
  commands can call `NotifyAction(...)` to surface status.

### Compare With… (Plan 23 #3, MVP)

- **File → Compare With…** prompts for a second XML file, parses it,
  and shows a structural diff report in a modal window. Diff is
  keyed by canonical XPath — added/removed elements and changed
  attribute/text values are surfaced. Order-of-children diffs
  are not surfaced separately in this MVP. Dual-pane side-by-side
  view is deferred to Plan 24.

### Attribute Cut / Copy / Paste (Plan 23 #4)

- Ctrl+C on an attribute row writes `name="value"` (entity-escaped) to
  the clipboard as plain text.
- Ctrl+V on an element row, with attribute-format text on the
  clipboard, adds or replaces that attribute via a new
  `InsertAttributeFromClipboardCommand`. Single undo step.
- Ctrl+X on an attribute deletes it via `DeleteAttributeCommand`.
- Element subtree cut/copy/paste (v0.11) still works — paste auto-detects
  attribute-format text vs. XML.

### Drag auto-scroll + drop tip (Plan 23 #5)

- While dragging a tree row, hovering within 24 dip of the top or
  bottom edge of the tree viewport auto-scrolls. Scrolling is
  step-wise (~1 row per DragOver tick).
- Drop tooltip shows the dragged element's label so deep drags
  don't get lost in the cursor.

### Breadcrumb ellipsization (Plan 23 #6)

- Breadcrumb collapses to `seg1 › seg2 › … › seg-n-2 › seg-n-1 › seg-n`
  when the ancestor chain exceeds 6 segments. The `…` placeholder
  is non-clickable; head/tail segments remain clickable.

### XPath history (Plan 23 #7)

- The XPath query bar (Ctrl+G) tracks submitted expressions per
  session, deduplicated, capped at 50.
- **Up arrow** in the bar's input cycles to older expressions.
- **Down arrow** cycles back / clears the input.
- History is **session-scoped** — not persisted across editor restarts
  in v0.13. Persisted history is a Plan 24 candidate.

### XPath function autocompletion (Plan 23 #8, MVP)

- Typing a word prefix in the XPath bar opens a popup with matching
  `fn:*` function names (case-insensitive).
- Up/Down navigate the popup; Tab accepts the selection; Escape closes.
- No parameter hints, no context-sensitive filtering, no `map:`/`array:`
  namespaces yet — those are Plan 24 candidates.
- The popup's Up/Down arrows take priority over history cycling
  while the popup is open.

## Smoke-test checklist (manual)

| Action | Expected | Actual |
|---|---|---|
| Right-click a row → Copy XPath | Status bar: "Copied XPath: …"; clipboard contains the XPath. | tbd |
| Paste invalid XML via Ctrl+V | Status bar: "Paste failed: malformed XML …". | tbd |
| Drag a row onto its own descendant | Status bar: "Move rejected: …". | tbd |
| File → Compare With… → pick a doc | Modal report opens listing additions/removals/changes by XPath. | tbd |
| Compare identical docs | Report shows "0 difference(s)." | tbd |
| Select an attribute, Ctrl+C | Status bar: "Copied attribute @name". | tbd |
| Select an element, Ctrl+V (attribute on clipboard) | Attribute added/replaced; single undo restores. | tbd |
| Drag near top of viewport | Tree auto-scrolls up. | tbd |
| Drag near bottom of viewport | Tree auto-scrolls down. | tbd |
| Navigate to a deep node (>6 ancestors) | Breadcrumb shows head/`…`/tail; head and tail clickable. | tbd |
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
- [x] ~~No drag auto-scroll / drop preview.~~
- [x] ~~Breadcrumb cannot ellipsize for deep trees.~~
- [x] ~~No XPath history.~~
- [x] ~~No XPath function autocompletion.~~ (MVP fn:* prefix popup)

### High friction (carried)

- [ ] **Dual-pane side-by-side compare view** with synced scrolling and inline change highlights. v0.13 ships a textual diff report only.
- [ ] **Persistent XPath history** across editor restarts (currently per-session).
- [ ] **Parameter hints + signature info** in XPath autocompletion.
- [ ] **Context-aware autocompletion** (axis names, element names from the active doc).
- [ ] **Drop preview ghost** that renders the dragged subtree shape near the cursor — currently a tooltip label only.

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
````

- [ ] **Step 2: Run the full test suite one last time**

Run: `dotnet test --nologo 2>&1 | tail -10`
Expected: all green.

- [ ] **Step 3: Single commit for the whole pack**

```bash
git add -A
git commit -m "$(cat <<'EOF'
feat(editor): v0.13 polish pack — Plan 23 (8 features)

1. Copy XPath context menu — right-click a tree row → copy canonical
   XPath to clipboard via XPathBuilder + ClipboardService.WriteTextAsync.

2. Failed-action status segment — new LastActionStatus primitive with
   TTL auto-clear. Wired to surface silent rejects from drag-drops,
   pastes, replaces. Generic NotifyAction(message) helper on the editor.

3. Compare With… (MVP) — new XmlDocumentDiff (XPath-keyed add/remove/
   change) + File menu command. Textual report in modal window; dual-
   pane diff view deferred to Plan 24.

4. Attribute Cut/Copy/Paste — AttributeClipboardFormat wire format
   (name="value", entity-escaped); InsertAttributeFromClipboardCommand
   (replace-if-exists, single undo); clipboard service text helpers.

5. Drag auto-scroll + drop tip — NodeListView DragOver auto-scrolls
   within 24 dip of viewport edges; tooltip shows dragged element.

6. Breadcrumb ellipsization — collapses to head/…/tail when >6 segments;
   `…` placeholder is non-clickable.

7. XPath history — per-session deduplicated LRU (50 entries); Up/Down
   arrows in the XPath bar cycle history.

8. XPath fn:* autocompletion (MVP) — popup with prefix-matched fn:*
   function names; Tab accepts, Escape closes, Up/Down navigate. No
   parameter hints / context filtering yet.

24 new unit tests across diff, attribute format, attribute command,
history, function list, and status. Full suite green.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Out of scope (deferred to later plans)

- Dual-pane side-by-side compare view with synced scrolling + inline highlights
- Persistent XPath history (session-scoped only in v0.13)
- Parameter hints, signature info, context-aware filtering in XPath autocompletion
- `map:`, `array:`, `xs:` namespace autocompletion
- Drop preview ghost rendering the actual subtree near the cursor
- Multi-row drag / cross-document drag
