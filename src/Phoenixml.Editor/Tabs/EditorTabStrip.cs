using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Phoenixml.Editor.ViewModels;

namespace Phoenixml.Editor.Tabs;

/// <summary>
/// Tab strip across the top of the editor. Each tab represents an open document.
/// Selection drives <see cref="EditorState.ActiveTab"/>; close button removes the
/// tab via <see cref="EditorState.CloseTab"/>.
/// </summary>
internal sealed class EditorTabStrip : TabStrip
{
    private readonly EditorState _state;

    /// <summary>Initializes a new <see cref="EditorTabStrip"/> bound to the given state.</summary>
    public EditorTabStrip(EditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;

        ItemsSource = _state.Tabs;
        SelectionChanged += OnSelectionChanged;
        _state.ActiveTabChanged += OnActiveTabChanged;

        ItemTemplate = new FuncDataTemplate<DocumentTab>((tab, _) =>
        {
            if (tab is null) return null!;

            var title = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0),
            };
            title.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(DocumentTab.Title)));

            var close = new Button
            {
                Content = "x",
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                Margin = new Thickness(2, 0, 0, 0),
                Background = Brushes.Transparent,
                BorderThickness = default,
                VerticalAlignment = VerticalAlignment.Center,
            };
            close.Click += (_, e) =>
            {
                e.Handled = true;
                _state.CloseTab(tab);
            };

            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { title, close },
            };
        });
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedItem is DocumentTab tab)
            _state.ActiveTab = tab;
    }

    private void OnActiveTabChanged(DocumentTab? tab)
    {
        if (!ReferenceEquals(SelectedItem, tab))
            SelectedItem = tab;
    }
}
