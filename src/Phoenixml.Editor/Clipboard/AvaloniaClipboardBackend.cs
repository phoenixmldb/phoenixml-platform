using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Phoenixml.Workbench.Clipboard;

namespace Phoenixml.Editor.Clipboard;

/// <summary>
/// Avalonia-backed <see cref="IClipboardBackend"/>. Translates the workbench's format-dictionary
/// contract into a <see cref="DataTransfer"/> for the underlying <see cref="IClipboard"/>.
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
        // One item carrying every format, so other apps see a single clipboard entry.
        var item = new DataTransferItem();
        foreach (var (format, value) in payload)
            item.Set(ToDataFormat(format), value);
#pragma warning disable CA2000 // Dispose objects — the clipboard owns the DataTransfer and disposes it when unused
        var data = new DataTransfer();
#pragma warning restore CA2000
        data.Add(item);
        return _inner.SetDataAsync(data);
    }

    public Task<string?> GetDataAsync(string format) =>
        _inner.TryGetValueAsync(ToDataFormat(format));

    public Task<string?> GetTextAsync() => _inner.TryGetTextAsync();

    public Task SetTextAsync(string text) => _inner.SetTextAsync(text);

    // Workbench keys are platform-visible formats (MIME types), so other applications can read them.
    private static DataFormat<string> ToDataFormat(string format) =>
        format == ClipboardFormats.Text ? DataFormat.Text : DataFormat.CreateStringPlatformFormat(format);
}
