using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Phoenixml.Editor.Lsp;

namespace Phoenixml.Editor.Views;

/// <summary>
/// Shows references returned by <c>textDocument/references</c>. Each row is a
/// line/column display; activating one raises <see cref="ReferenceActivated"/>.
/// </summary>
public sealed partial class ReferencesView : UserControl
{
    public ReferencesView()
    {
        InitializeComponent();
        var list = this.FindControl<ListBox>("ReferenceList")!;
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ReferenceItem item)
                ReferenceActivated?.Invoke(this, item.Location);
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

#pragma warning disable CA1003
    public event EventHandler<LspLocation>? ReferenceActivated;
#pragma warning restore CA1003

    public void SetReferences(IReadOnlyList<LspLocation> refs)
    {
        var list = this.FindControl<ListBox>("ReferenceList")!;
        list.ItemsSource = refs
            .Select(r => new ReferenceItem(
                $"line {r.Range.Start.Line + 1}, col {r.Range.Start.Character + 1}",
                r))
            .ToArray();
    }

    private sealed record ReferenceItem(string Label, LspLocation Location);
}
