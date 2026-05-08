using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Abstractions.Editing;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Validation;

namespace Phoenixml.Editor.Validation;

/// <summary>
/// Validates the active document against its resolved schema after a 500ms debounce.
/// </summary>
/// <remarks>
/// <para>
/// Subscribes to the active document's <see cref="Phoenixml.Platform.Documents.Document.Changed"/> event,
/// debounces rapid changes with a 500ms window, then runs
/// <see cref="XmlSchemaValidator.Validate"/> synchronously on the caller's thread.
/// </para>
/// <para>
/// <b>Threading:</b> <see cref="XmlDocument"/>._byId is a plain <see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/>
/// with no synchronization. Running validation on a <c>Task.Run</c> worker while the UI thread
/// may be mutating <c>_byId</c> (via undo/redo commands) would race. Validation is therefore
/// run synchronously after the debounce delay, on whatever thread resumes from
/// <c>await Task.Delay</c>. The host (<c>EditorMainWindow</c>) always calls
/// <see cref="SetActiveDocument"/> from the Avalonia UI thread, so the resumed continuation
/// runs on the UI thread when Avalonia's <c>SynchronizationContext</c> is present.
/// Plan 13 may revisit this if very large documents make synchronous validation user-visible —
/// the likely fix is a thread-safe snapshot of the relevant subset of _byId before handing off.
/// </para>
/// </remarks>
internal sealed class EditorValidationService : IDisposable
{
    private readonly SchemaResolver _resolver;
    private XmlDocument? _activeDoc;
    private CancellationTokenSource? _debounce;

    /// <summary>Gets the most recently published diagnostics list.</summary>
    public IReadOnlyList<IDiagnostic> Current { get; private set; } = [];

    /// <summary>
    /// Initializes a new <see cref="EditorValidationService"/> with the given schema resolver.
    /// </summary>
    /// <param name="resolver">The resolver used to locate XSD schemas for documents.</param>
    public EditorValidationService(SchemaResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    /// <summary>
    /// Raised after each validation pass (including passes that produce an empty list).
    /// Subscribers update their UI to reflect the new diagnostics.
    /// </summary>
#pragma warning disable CA1003 // Action<T> design is intentional per existing codebase conventions
    public event Action<IReadOnlyList<IDiagnostic>>? DiagnosticsChanged;
#pragma warning restore CA1003

    /// <summary>
    /// Sets the active document. Unsubscribes from the previous document's Changed event,
    /// subscribes to the new one, and schedules an immediate validation pass.
    /// Pass <see langword="null"/> to clear the active document (diagnostics will be cleared).
    /// </summary>
    /// <param name="doc">The new active document, or <see langword="null"/>.</param>
    public void SetActiveDocument(XmlDocument? doc)
    {
        if (_activeDoc is not null) _activeDoc.Changed -= OnChanged;
        _activeDoc = doc;
        if (_activeDoc is not null) _activeDoc.Changed += OnChanged;
        ScheduleValidation();
    }

    private void OnChanged(DocumentChange _) => ScheduleValidation();

    private void ScheduleValidation()
    {
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        var ct = _debounce.Token;
        _ = RunDebouncedAsync(ct);
    }

    /// <summary>
    /// Exposed for unit tests that want to drive the debounce cycle directly without
    /// relying on wall-clock delays.
    /// </summary>
    internal async Task RunDebouncedAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(500, ct);
            ct.ThrowIfCancellationRequested();

            var doc = _activeDoc;
            if (doc is null)
            {
                Publish([]);
                return;
            }

            var schemas = _resolver.Resolve(doc);
            if (schemas is null)
            {
                Publish([]);
                return;
            }

            // Synchronous on the resuming thread — see class-level threading notes.
            var diags = XmlSchemaValidator.Validate(doc, schemas);
            ct.ThrowIfCancellationRequested();
            Publish(diags);
        }
        catch (OperationCanceledException)
        {
            // This pass was superseded by a newer change — discard silently.
        }
#pragma warning disable CA1031 // Intentional: validation must never crash the editor
        catch (Exception)
#pragma warning restore CA1031
        {
            // Surface as a single error diagnostic so the user knows something went wrong.
            Publish([new XmlDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message = "Validation failed unexpectedly.",
            }]);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_activeDoc is not null) _activeDoc.Changed -= OnChanged;
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = null;
    }

    private void Publish(IReadOnlyList<IDiagnostic> diags)
    {
        Current = diags;
        DiagnosticsChanged?.Invoke(diags);
    }
}
