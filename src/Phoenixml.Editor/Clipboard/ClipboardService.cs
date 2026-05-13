using System;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace Phoenixml.Editor.Clipboard;

/// <summary>
/// Internal seam over Avalonia <see cref="IClipboard"/> that covers only the three methods
/// <see cref="ClipboardService"/> requires. Introducing this interface works around
/// Avalonia's <c>[NotClientImplementable]</c> restriction on <see cref="IClipboard"/>,
/// which prevents direct stub implementations in tests.
/// </summary>
internal interface IClipboardBackend
{
    /// <summary>Places a <see cref="IDataObject"/> onto the clipboard.</summary>
    Task SetDataObjectAsync(IDataObject data);

    /// <summary>
    /// Returns the object stored under <paramref name="format"/>, or
    /// <see langword="null"/> when the format is absent.
    /// </summary>
    Task<object?> GetDataAsync(string format);

    /// <summary>Returns the plain-text content of the clipboard, or <see langword="null"/>.</summary>
    Task<string?> GetTextAsync();
}

/// <summary>
/// Production <see cref="IClipboardBackend"/> that delegates to an Avalonia
/// <see cref="IClipboard"/> instance.
/// </summary>
internal sealed class AvaloniaClipboardBackend : IClipboardBackend
{
    private readonly IClipboard _inner;

    /// <summary>Wraps <paramref name="clipboard"/>.</summary>
    internal AvaloniaClipboardBackend(IClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        _inner = clipboard;
    }

    /// <inheritdoc/>
    public Task SetDataObjectAsync(IDataObject data) => _inner.SetDataObjectAsync(data);

    /// <inheritdoc/>
    public Task<object?> GetDataAsync(string format) => _inner.GetDataAsync(format);

    /// <inheritdoc/>
    public Task<string?> GetTextAsync() => _inner.GetTextAsync();
}

/// <summary>
/// Thin wrapper around Avalonia's <see cref="IClipboard"/> that exposes two XML-oriented
/// operations: writing XML text to the clipboard in two formats and reading it back with
/// a format-preference fallback.
/// </summary>
/// <remarks>
/// <para>
/// <b>Write:</b> <see cref="WriteXmlAsync"/> puts the XML string onto the clipboard as both
/// <c>application/xml</c> (for XML-aware tools) and <c>text/plain</c> (for any other tool).
/// </para>
/// <para>
/// <b>Read:</b> <see cref="ReadXmlAsync"/> prefers <c>application/xml</c>; if that format is
/// absent it falls back to <c>text/plain</c>. This allows users to paste XML that was copied
/// from any source — another XML editor, a browser, a terminal — not just from Phoenixml.
/// </para>
/// <para>
/// The service takes an <see cref="IClipboardBackend"/> as a constructor dependency so it can
/// be tested with a stub (no running Avalonia application required).
/// </para>
/// </remarks>
internal sealed class ClipboardService
{
    private readonly IClipboardBackend _backend;

    /// <summary>
    /// Creates a <see cref="ClipboardService"/> backed by <paramref name="backend"/>.
    /// </summary>
    /// <param name="backend">Backend that forwards calls to the platform clipboard.</param>
    /// <exception cref="ArgumentNullException"><paramref name="backend"/> is <see langword="null"/>.</exception>
    internal ClipboardService(IClipboardBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    /// <summary>
    /// Factory helper: creates a <see cref="ClipboardService"/> that wraps an Avalonia
    /// <see cref="IClipboard"/> instance.
    /// </summary>
    /// <param name="clipboard">The Avalonia clipboard obtained from <c>TopLevel.Clipboard</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clipboard"/> is <see langword="null"/>.</exception>
    internal static ClipboardService FromAvalonia(IClipboard clipboard) =>
        new(new AvaloniaClipboardBackend(clipboard));

    /// <summary>
    /// Writes <paramref name="xml"/> to the clipboard as both <c>application/xml</c>
    /// and <c>text/plain</c>.
    /// </summary>
    /// <param name="xml">The XML string to place on the clipboard.</param>
    /// <exception cref="ArgumentNullException"><paramref name="xml"/> is <see langword="null"/>.</exception>
    public async Task WriteXmlAsync(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var data = new DataObject();
        data.Set("application/xml", xml);
        data.Set(DataFormats.Text, xml);
        await _backend.SetDataObjectAsync(data).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads XML from the clipboard.
    /// Prefers <c>application/xml</c>; falls back to <c>text/plain</c> when the
    /// structured format is absent. Returns <see langword="null"/> when the clipboard
    /// contains no usable text in either format.
    /// </summary>
    /// <returns>
    /// The clipboard XML string, or <see langword="null"/> if nothing usable is present.
    /// </returns>
    public async Task<string?> ReadXmlAsync()
    {
        if (await _backend.GetDataAsync("application/xml").ConfigureAwait(false) is string xml
            && !string.IsNullOrEmpty(xml))
            return xml;

        var text = await _backend.GetTextAsync().ConfigureAwait(false);
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
