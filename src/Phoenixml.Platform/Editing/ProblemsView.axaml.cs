using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Phoenixml.Platform.Abstractions.Editing;

namespace Phoenixml.Platform.Editing;

/// <summary>
/// Displays a list of <see cref="IDiagnostic"/> entries with severity icons.
/// Double-click a row to raise <see cref="DiagnosticActivated"/>; the host navigates
/// to <c>diagnostic.Location</c>.
/// </summary>
public sealed partial class ProblemsView : UserControl
{
    /// <summary>Initializes a new instance of <see cref="ProblemsView"/>.</summary>
    public ProblemsView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Raised when the user double-clicks a diagnostic row.</summary>
#pragma warning disable CA1003 // Use generic event handler instances — intentional; Action<T> pattern
    public event Action<IDiagnostic>? DiagnosticActivated;
#pragma warning restore CA1003

    /// <summary>Replaces the diagnostics displayed in the list.</summary>
    public void SetDiagnostics(IReadOnlyList<IDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var rows = diagnostics.Select(d => new DiagnosticRow(d)).ToList();
        var host = this.FindControl<ListBox>("DiagnosticsHost");
        if (host is not null) host.ItemsSource = rows;
    }

    private void DiagnosticsHost_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is DiagnosticRow row)
            DiagnosticActivated?.Invoke(row.Source);
    }
}

/// <summary>Row view-model wrapping an <see cref="IDiagnostic"/> for the DataTemplate.</summary>
internal sealed class DiagnosticRow
{
    /// <summary>The underlying diagnostic.</summary>
    public IDiagnostic Source { get; }

    /// <summary>Severity glyph character to display.</summary>
    public string SeverityGlyph { get; }

    /// <summary>Brush for the severity glyph, resolved from theme tokens.</summary>
    public IBrush SeverityBrush { get; }

    /// <summary>Diagnostic code, or an empty string if uncoded.</summary>
    public string Code { get; }

    /// <summary>True when a code string is available.</summary>
    public bool HasCode { get; }

    /// <summary>Human-readable message.</summary>
    public string Message { get; }

    internal DiagnosticRow(IDiagnostic source)
    {
        Source = source;
        Message = source.Message;
        Code = source.Code ?? string.Empty;
        HasCode = source.Code is not null;

        switch (source.Severity)
        {
            case DiagnosticSeverity.Info:
                SeverityGlyph = "ⓘ";
                SeverityBrush = new SolidColorBrush(Color.Parse("#0078D4"));
                break;
            case DiagnosticSeverity.Warning:
                SeverityGlyph = "⚠";
                SeverityBrush = new SolidColorBrush(Color.Parse("#CA8B00"));
                break;
            case DiagnosticSeverity.Error:
                SeverityGlyph = "✕";
                SeverityBrush = new SolidColorBrush(Color.Parse("#C42B1C"));
                break;
            default:
                SeverityGlyph = "?";
                SeverityBrush = Brushes.Gray;
                break;
        }
    }
}
