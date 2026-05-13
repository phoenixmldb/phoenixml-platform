using System;
using System.Text.RegularExpressions;

namespace Phoenixml.Editor.Clipboard;

/// <summary>
/// Serialization shim for attribute-clipboard wire format: <c>name="value"</c> with the
/// value entity-escaped (<c>&amp;quot;</c>, <c>&amp;amp;</c>, <c>&amp;lt;</c>, <c>&amp;gt;</c>).
/// Plain-text-clipboard friendly so users can paste into editors that don't understand XML.
/// </summary>
public static class AttributeClipboardFormat
{
    private static readonly Regex Parser = new(
        "^(?<name>[^\\s=\"]+)=\"(?<value>[^\"]*)\"$",
        RegexOptions.Compiled);

    /// <summary>Serializes a name/value pair to the clipboard wire format.</summary>
    public static string Serialize(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        var escaped = value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
        return $"{name}=\"{escaped}\"";
    }

    /// <summary>Parses the wire format. Returns <see langword="false"/> when malformed.</summary>
    public static bool TryParse(string text, out string name, out string value)
    {
        name = string.Empty;
        value = string.Empty;
        if (string.IsNullOrEmpty(text)) return false;
        var m = Parser.Match(text);
        if (!m.Success) return false;
        name = m.Groups["name"].Value;
        var raw = m.Groups["value"].Value;
        value = raw
            .Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal);
        return true;
    }
}
