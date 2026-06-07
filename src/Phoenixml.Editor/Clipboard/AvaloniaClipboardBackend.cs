using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Phoenixml.Workbench.Clipboard;

namespace Phoenixml.Editor.Clipboard;

/// <summary>
/// Avalonia-backed <see cref="IClipboardBackend"/>. Translates the workbench's format-dictionary
/// contract into a <see cref="DataObject"/> for the underlying <see cref="IClipboard"/>.
/// Lives in Editor so Workbench stays free of Avalonia references.
/// </summary>
internal sealed class AvaloniaClipboardBackend : IClipboardBackend
{
    private readonly IClipboard _inner;

    internal AvaloniaClipboardBackend(IClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        _inner = clipboard;
    }

    /// <summary>Convenience factory used by EditorMainWindow once <c>TopLevel.Clipboard</c> is live.</summary>
    internal static ClipboardService FromAvalonia(IClipboard clipboard) =>
        new(new AvaloniaClipboardBackend(clipboard));

    public Task SetFormatsAsync(IReadOnlyDictionary<string, string> payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var data = new DataObject();
        foreach (var (format, value) in payload)
            data.Set(format, value);
        return _inner.SetDataObjectAsync(data);
    }

    public async Task<string?> GetDataAsync(string format) =>
        await _inner.GetDataAsync(format).ConfigureAwait(false) as string;

    public Task<string?> GetTextAsync() => _inner.GetTextAsync();

    public Task SetTextAsync(string text) => _inner.SetTextAsync(text);
}
