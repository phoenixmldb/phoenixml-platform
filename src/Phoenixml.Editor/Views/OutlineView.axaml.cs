using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Phoenixml.Workbench.Lsp;

namespace Phoenixml.Editor.Views;

/// <summary>
/// Shows the document's top-level symbols (functions, variables, templates) as
/// returned by the LSP server's <c>textDocument/documentSymbol</c>. Clicking a
/// symbol raises <see cref="SymbolActivated"/> with its range.
/// </summary>
public sealed partial class OutlineView : UserControl
{
    /// <summary>Initializes a new <see cref="OutlineView"/>.</summary>
    public OutlineView()
    {
        InitializeComponent();
        var list = this.FindControl<ListBox>("SymbolList")!;
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is LspDocumentSymbol sym)
                SymbolActivated?.Invoke(this, sym);
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

#pragma warning disable CA1003
    /// <summary>Raised when the user picks a symbol from the list.</summary>
    public event EventHandler<LspDocumentSymbol>? SymbolActivated;
#pragma warning restore CA1003

    /// <summary>Replaces the displayed symbols.</summary>
    public void SetSymbols(IReadOnlyList<LspDocumentSymbol> symbols)
    {
        var list = this.FindControl<ListBox>("SymbolList")!;
        list.ItemsSource = symbols;
    }
}
