using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Phoenixml.Workbench.Clipboard;

/// <summary>
/// XML-aware clipboard facade. Writes XML under two formats (<c>application/xml</c> and plain text)
/// and reads it back with format-preference fallback so users can paste XML copied from any source.
/// All UI dependencies are encapsulated in the injected <see cref="IClipboardBackend"/>.
/// </summary>
public sealed class ClipboardService
{
    private readonly IClipboardBackend _backend;

    /// <summary>Wrap <paramref name="backend"/>.</summary>
    public ClipboardService(IClipboardBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    /// <summary>Write <paramref name="xml"/> under both <c>application/xml</c> and plain text formats.</summary>
    public async Task WriteXmlAsync(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ClipboardFormats.Xml] = xml,
            [ClipboardFormats.Text] = xml,
        };
        await _backend.SetFormatsAsync(payload).ConfigureAwait(false);
    }

    /// <summary>
    /// Read XML from the clipboard, preferring <c>application/xml</c> and falling back to plain text.
    /// Returns <c>null</c> when the clipboard has no usable content in either format.
    /// </summary>
    public async Task<string?> ReadXmlAsync()
    {
        var xml = await _backend.GetDataAsync(ClipboardFormats.Xml).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(xml)) return xml;

        var text = await _backend.GetTextAsync().ConfigureAwait(false);
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>Write plain text to the clipboard.</summary>
    public async Task WriteTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        await _backend.SetTextAsync(text).ConfigureAwait(false);
    }

    /// <summary>Read plain text from the clipboard, or <c>null</c> when empty.</summary>
    public async Task<string?> ReadTextAsync()
    {
        var text = await _backend.GetTextAsync().ConfigureAwait(false);
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
