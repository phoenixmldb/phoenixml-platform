using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Phoenixml.Platform.Editing;

/// <summary>
/// A basic TextBox-backed text editor. Multiline, monospace font, no wrapping.
/// </summary>
/// <remarks>
/// AvaloniaEdit integration (line numbers, syntax coloring, intellisense popup) is deferred
/// to Plan 5 — XML syntax coloring requires an XML lexer that lives in
/// <c>Phoenixml.Platform.Editor.Xml</c>. v0.3 ships a usable plain-text editor.
/// </remarks>
public sealed partial class TextEditorView : UserControl
{
    private bool _suppressEvent;

    /// <summary>Initializes a new instance of <see cref="TextEditorView"/>.</summary>
    public TextEditorView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Raised when the text content changes.</summary>
#pragma warning disable CA1003 // Use generic event handler instances — intentional; Action<T> pattern
    public event Action<string>? TextChanged;
#pragma warning restore CA1003

    /// <summary>Gets or sets the full text content.</summary>
    public string Text
    {
        get
        {
            var box = this.FindControl<TextBox>("EditorBox");
            return box?.Text ?? string.Empty;
        }
        set
        {
            var box = this.FindControl<TextBox>("EditorBox");
            if (box is null) return;

            _suppressEvent = true;
            try { box.Text = value; }
            finally { _suppressEvent = false; }
        }
    }

    /// <summary>Gets or sets a value indicating whether the editor is read-only.</summary>
    public bool IsReadOnly
    {
        get
        {
            var box = this.FindControl<TextBox>("EditorBox");
            return box?.IsReadOnly ?? false;
        }
        set
        {
            var box = this.FindControl<TextBox>("EditorBox");
            if (box is not null) box.IsReadOnly = value;
        }
    }

    private void EditorBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressEvent) return;
        if (sender is TextBox box)
            TextChanged?.Invoke(box.Text ?? string.Empty);
    }
}
