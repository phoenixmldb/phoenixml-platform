using System.Collections.Generic;
using System.Threading.Tasks;

namespace Phoenixml.Workbench.Clipboard;

/// <summary>
/// Shell-neutral clipboard seam. Shell adapters implement this against their native clipboard
/// (Avalonia IClipboard, Godot DisplayServer, etc.). Tests can stub it with an in-memory dictionary.
/// </summary>
public interface IClipboardBackend
{
    /// <summary>
    /// Place the same payload on the clipboard under multiple format keys at once
    /// (e.g. <c>application/xml</c> + <c>text/plain</c>). Replaces any prior clipboard contents.
    /// </summary>
    Task SetFormatsAsync(IReadOnlyDictionary<string, string> payload);

    /// <summary>Read the value stored under <paramref name="format"/>, or <c>null</c> if absent.</summary>
    Task<string?> GetDataAsync(string format);

    /// <summary>Read the plain-text clipboard contents, or <c>null</c> when empty.</summary>
    Task<string?> GetTextAsync();

    /// <summary>Place plain text on the clipboard.</summary>
    Task SetTextAsync(string text);
}

/// <summary>Well-known clipboard format keys used by the workbench.</summary>
public static class ClipboardFormats
{
    /// <summary>The well-known MIME for plain text — matches Avalonia <c>DataFormats.Text</c>.</summary>
    public const string Text = "Text";

    /// <summary>Structured XML format used by Phoenixml for fidelity round-trips.</summary>
    public const string Xml = "application/xml";
}
