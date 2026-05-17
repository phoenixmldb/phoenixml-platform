using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using AvaloniaEdit;
using Phoenixml.Editor.Lsp;
using Phoenixml.Editor.Views.SyntaxHighlighting;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Editor.Text;

namespace Phoenixml.Editor.Views;

/// <summary>
/// AvaloniaEdit-based view that displays a <see cref="TextDocument"/>. Two-way bound:
/// typing fires <see cref="TextDocument.ReplaceAll"/>; external edits replace the
/// editor's buffer. Hosts LSP completion + hover popups (Plan 27).
/// </summary>
public sealed partial class TextEditorView : UserControl
{
    private TextDocument? _document;
    private LspSession? _session;
    private bool _suppress;
    private CancellationTokenSource? _hoverCts;
    private readonly HighlightingRegistry _highlighting = new();

    /// <summary>Initializes a new <see cref="TextEditorView"/>.</summary>
    public TextEditorView() => InitializeComponent();

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        var editor = this.FindControl<TextEditor>("Editor")!;
        editor.AddHandler(KeyDownEvent, OnEditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        editor.TextArea.Caret.PositionChanged += OnCaretMoved;
        // Plan 28 #8: enable AvaloniaEdit's built-in search panel (Ctrl+F).
        AvaloniaEdit.Search.SearchPanel.Install(editor);
    }

    /// <summary>Binds the view to <paramref name="document"/>; optional <paramref name="session"/> drives LSP UX.</summary>
    public void Bind(TextDocument document, LspSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var editor = this.FindControl<TextEditor>("Editor")!;

        if (_document is not null)
        {
            _document.Changed -= OnDocumentChanged;
            editor.Document.TextChanged -= OnEditorTextChanged;
        }

        _document = document;
        _session = session;
        _suppress = true;
        try { editor.Document.Text = document.Text; }
        finally { _suppress = false; }

        editor.SyntaxHighlighting = _highlighting.Resolve(document.Kind);

        editor.Document.TextChanged += OnEditorTextChanged;
        document.Changed += OnDocumentChanged;
    }

    /// <summary>Bridges Ctrl+Z to AvaloniaEdit's undo (Plan 27 #7).</summary>
    public bool CanUndo => this.FindControl<TextEditor>("Editor")?.CanUndo ?? false;
    /// <summary>True when AvaloniaEdit's redo stack has entries.</summary>
    public bool CanRedo => this.FindControl<TextEditor>("Editor")?.CanRedo ?? false;
    /// <summary>Routes Ctrl+Z to the embedded editor's undo stack.</summary>
    public void Undo() => this.FindControl<TextEditor>("Editor")?.Undo();
    /// <summary>Routes Ctrl+Y to the embedded editor's redo stack.</summary>
    public void Redo() => this.FindControl<TextEditor>("Editor")?.Redo();

    /// <summary>Moves the caret to (line, column) (0-based) and scrolls into view.</summary>
    public void MoveCaretTo(int line, int column)
    {
        var editor = this.FindControl<TextEditor>("Editor");
        if (editor is null) return;
        var lineNumber = Math.Max(1, line + 1);
        if (lineNumber > editor.Document.LineCount) lineNumber = editor.Document.LineCount;
        var docLine = editor.Document.GetLineByNumber(lineNumber);
        var offset = docLine.Offset + Math.Min(column, docLine.Length);
        editor.CaretOffset = offset;
        editor.TextArea.Caret.BringCaretToView();
        editor.Focus();
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppress || _document is null) return;
        var editor = this.FindControl<TextEditor>("Editor")!;
        _document.ReplaceAll(editor.Document.Text);
    }

    private void OnDocumentChanged(DocumentChange change)
    {
        _ = change;
        if (_document is null) return;
        var editor = this.FindControl<TextEditor>("Editor")!;
        if (editor.Document.Text == _document.Text) return;
        _suppress = true;
        try { editor.Document.Text = _document.Text; }
        finally { _suppress = false; }
    }

    // -------------------------------------------------------------------------
    // Completion popup (Plan 27 #4)
    // -------------------------------------------------------------------------

    private async void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        var popup = this.FindControl<Popup>("CompletionPopup")!;
        var list = this.FindControl<ListBox>("CompletionList")!;

        if (popup.IsOpen)
        {
            if (e.Key == Key.Tab || e.Key == Key.Enter)
            {
                AcceptCompletion(list);
                popup.IsOpen = false;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                popup.IsOpen = false;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up && list.SelectedIndex > 0)
            {
                list.SelectedIndex--;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Down)
            {
                var count = (list.ItemsSource as System.Collections.ICollection)?.Count ?? 0;
                if (list.SelectedIndex < count - 1)
                {
                    list.SelectedIndex++;
                    e.Handled = true;
                    return;
                }
            }
        }

        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            await ShowCompletionAsync();
            e.Handled = true;
            return;
        }

        // Plan 28 #5: signatureHelp on `(`. Let the `(` insert first, then ask the server.
        if (e.KeySymbol == "(")
        {
            await Task.Yield();
            await ShowSignatureHelpAsync();
            return;
        }

        // Plan 28 #6: F12 → go to definition.
        if (e.Key == Key.F12 && (e.KeyModifiers & KeyModifiers.Shift) == 0)
        {
            await GoToDefinitionAsync();
            e.Handled = true;
            return;
        }

        // Plan 29: Shift+F12 → find references.
        if (e.Key == Key.F12 && (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift)
        {
            await FindReferencesAsync();
            e.Handled = true;
        }
    }

    private async Task FindReferencesAsync()
    {
        if (_session is null) return;
        var editor = this.FindControl<TextEditor>("Editor")!;
        var caret = editor.TextArea.Caret;
        var refs = await _session.RequestReferencesAsync(caret.Line - 1, caret.Column - 1);
        ReferencesRequested?.Invoke(this, refs);
    }

#pragma warning disable CA1003
    /// <summary>Raised when the user invokes Find References (Shift+F12).</summary>
    public event EventHandler<Lsp.LspLocation[]>? ReferencesRequested;
#pragma warning restore CA1003

    private async Task ShowSignatureHelpAsync()
    {
        if (_session is null) return;
        var editor = this.FindControl<TextEditor>("Editor")!;
        var caret = editor.TextArea.Caret;
        var help = await _session.RequestSignatureHelpAsync(caret.Line - 1, caret.Column - 1);
        var popup = this.FindControl<Popup>("SignatureHelpPopup")!;
        var text = this.FindControl<TextBlock>("SignatureText")!;
        if (help is null || help.Signatures.Length == 0)
        {
            popup.IsOpen = false;
            return;
        }
        text.Text = help.Signatures[help.ActiveSignature].Label;
        AnchorPopupAtCaret(popup);
        popup.IsOpen = true;
    }

    private async Task GoToDefinitionAsync()
    {
        if (_session is null) return;
        var editor = this.FindControl<TextEditor>("Editor")!;
        var caret = editor.TextArea.Caret;
        var loc = await _session.RequestDefinitionAsync(caret.Line - 1, caret.Column - 1);
        if (loc is null) return;
        MoveCaretTo(loc.Range.Start.Line, loc.Range.Start.Character);
    }

    private async Task ShowCompletionAsync()
    {
        if (_session is null) return;
        var editor = this.FindControl<TextEditor>("Editor")!;
        var caret = editor.TextArea.Caret;
        var labels = await _session.RequestCompletionAsync(caret.Line - 1, caret.Column - 1);
        if (labels.Count == 0) return;

        var popup = this.FindControl<Popup>("CompletionPopup")!;
        var list = this.FindControl<ListBox>("CompletionList")!;
        list.ItemsSource = labels;
        list.SelectedIndex = 0;
        AnchorPopupAtCaret(popup);
        popup.IsOpen = true;
    }

    private void AcceptCompletion(ListBox list)
    {
        if (list.SelectedItem is not string label) return;
        var editor = this.FindControl<TextEditor>("Editor")!;
        var caret = editor.TextArea.Caret;
        var line = editor.Document.GetLineByNumber(caret.Line);
        var lineText = editor.Document.GetText(line);
        var offsetInLine = caret.Column - 1;
        int start = offsetInLine;
        while (start > 0 && IsWordChar(lineText[start - 1])) start--;
        var replaceFrom = line.Offset + start;
        var replaceLength = offsetInLine - start;
        editor.Document.Replace(replaceFrom, replaceLength, label);
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ':' || c == '$';

    /// <summary>
    /// Anchors a popup at the caret's pixel position within the editor (Plan 29 #7).
    /// On failure (AvaloniaEdit API drift, no visual yet), falls back to Pointer placement
    /// so popups never crash the UI.
    /// </summary>
    private void AnchorPopupAtCaret(Popup popup)
    {
        var editor = this.FindControl<TextEditor>("Editor")!;
        try
        {
            var caret = editor.TextArea.Caret;
            var textView = editor.TextArea.TextView;
            var visual = textView.GetVisualPosition(
                new AvaloniaEdit.TextViewPosition(caret.Line, caret.Column),
                AvaloniaEdit.Rendering.VisualYPosition.LineBottom);
            var docPoint = visual - textView.ScrollOffset;
            popup.PlacementRect = new Avalonia.Rect(docPoint.X, docPoint.Y, 1, 1);
            popup.HorizontalOffset = 0;
            popup.VerticalOffset = 0;
        }
        catch (InvalidOperationException) { popup.Placement = PlacementMode.Pointer; }
        catch (ArgumentException) { popup.Placement = PlacementMode.Pointer; }
        catch (NullReferenceException) { popup.Placement = PlacementMode.Pointer; }
    }

    // -------------------------------------------------------------------------
    // Hover popup (Plan 27 #5)
    // -------------------------------------------------------------------------

    private void OnCaretMoved(object? sender, EventArgs e)
    {
        _hoverCts?.Cancel();
        _hoverCts = new CancellationTokenSource();
        var ct = _hoverCts.Token;
        _ = ShowHoverAfterDelayAsync(ct);
    }

    private async Task ShowHoverAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(500, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            if (_session is null) return;
            var editor = this.FindControl<TextEditor>("Editor")!;
            var caret = editor.TextArea.Caret;
            var content = await _session.RequestHoverAsync(caret.Line - 1, caret.Column - 1);
            ct.ThrowIfCancellationRequested();

            var popup = this.FindControl<Popup>("HoverPopup")!;
            var text = this.FindControl<TextBlock>("HoverText")!;
            if (string.IsNullOrEmpty(content))
            {
                popup.IsOpen = false;
                return;
            }
            text.Text = content;
            AnchorPopupAtCaret(popup);
            popup.IsOpen = true;
        }
        catch (OperationCanceledException) { }
    }
}
