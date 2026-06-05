using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Workbench.ViewModels;

// Workbench shell-agnosticism spike.
//
// If EditorState's TabAdded / ActiveDocumentChanged / TabClosed events fire
// correctly from a console process — with no Avalonia, no Godot, no UI
// dispatcher anywhere — then the workbench layer truly holds no shell
// assumptions. Any host that can subscribe to events can drive it.

var state = new EditorState();

state.TabAdded += (_, tab) =>
    Console.WriteLine($"  ▸ TabAdded:              {tab.Document.DisplayName}");
state.TabClosed += (_, tab) =>
    Console.WriteLine($"  ▸ TabClosed:             {tab.Document.DisplayName}");
state.ActiveTabChanged += (_, tab) =>
    Console.WriteLine($"  ▸ ActiveTabChanged:      {tab?.Document.DisplayName ?? "<none>"}");
state.ActiveDocumentChanged += (_, doc) =>
    Console.WriteLine($"  ▸ ActiveDocumentChanged: {doc?.DisplayName ?? "<none>"}");

Console.WriteLine("== workbench spike ==");
Console.WriteLine($"shell:  console (no UI framework loaded)");
Console.WriteLine($"state:  {state.GetType().FullName}");
Console.WriteLine($"tabs:   {state.Tabs.Count}");
Console.WriteLine();

// Write two throwaway XML files to a scratch dir, open both, swap active,
// then close one. Exercise the full event surface.
var scratchDir = Path.Combine(Path.GetTempPath(), "phoenixml-workbench-spike");
Directory.CreateDirectory(scratchDir);
var firstPath = Path.Combine(scratchDir, "first.xml");
var secondPath = Path.Combine(scratchDir, "second.xml");
await File.WriteAllTextAsync(firstPath,
    "<library>\n  <book id=\"1\"><title>The Iliad</title></book>\n</library>\n",
    CancellationToken.None);
await File.WriteAllTextAsync(secondPath,
    "<config>\n  <setting name=\"theme\" value=\"dark\" />\n</config>\n",
    CancellationToken.None);

Console.WriteLine("→ opening first.xml");
var firstDoc = await XmlDocument.LoadAsync(firstPath, CancellationToken.None);
var firstTab = state.OpenDocument(firstDoc);
Console.WriteLine();

Console.WriteLine("→ opening second.xml");
var secondDoc = await XmlDocument.LoadAsync(secondPath, CancellationToken.None);
state.OpenDocument(secondDoc);
Console.WriteLine();

Console.WriteLine("→ switching active back to first.xml");
state.ActiveTab = firstTab;
Console.WriteLine();

Console.WriteLine("→ closing first.xml");
state.CloseTab(firstTab);
Console.WriteLine();

Console.WriteLine($"final: {state.Tabs.Count} tab(s), active = {state.ActiveTab?.Document.DisplayName ?? "<none>"}");
Console.WriteLine("== spike complete — workbench drove all events from a console process ==");
