using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaEdit;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Editor.Text;

namespace Phoenixml.Editor.Views;

/// <summary>
/// AvaloniaEdit-based view that displays a <see cref="TextDocument"/>. Two-way bound:
/// typing fires <see cref="TextDocument.ReplaceAll"/>; external edits (e.g. revert)
/// replace the editor's buffer. A <c>_suppress</c> flag prevents feedback loops.
/// </summary>
public sealed partial class TextEditorView : UserControl
{
    private TextDocument? _document;
    private bool _suppress;

    /// <summary>Initializes a new <see cref="TextEditorView"/>.</summary>
    public TextEditorView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Binds the view to <paramref name="document"/>. Replaces any prior binding.</summary>
    public void Bind(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var editor = this.FindControl<TextEditor>("Editor")!;

        if (_document is not null)
        {
            _document.Changed -= OnDocumentChanged;
            editor.Document.TextChanged -= OnEditorTextChanged;
        }

        _document = document;
        _suppress = true;
        try { editor.Document.Text = document.Text; }
        finally { _suppress = false; }

        editor.Document.TextChanged += OnEditorTextChanged;
        document.Changed += OnDocumentChanged;
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppress || _document is null) return;
        var editor = this.FindControl<TextEditor>("Editor")!;
        _document.ReplaceAll(editor.Document.Text);
    }

    private void OnDocumentChanged(DocumentChange change)
    {
        if (_document is null) return;
        var editor = this.FindControl<TextEditor>("Editor")!;
        // Skip if the buffer already matches — we just sent this edit.
        if (editor.Document.Text == _document.Text) return;
        _suppress = true;
        try { editor.Document.Text = _document.Text; }
        finally { _suppress = false; }
    }
}
