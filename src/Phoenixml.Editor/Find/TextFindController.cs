using System;
using System.Collections.Generic;

namespace Phoenixml.Editor.Find;

/// <summary>
/// Pure-logic find/replace over plain text. Returns (offset, length) ranges
/// suitable for selection-range highlighting in AvaloniaEdit. No UI dependencies.
/// </summary>
public static class TextFindController
{
    /// <summary>Returns all match ranges in <paramref name="text"/>. Empty when query is null/empty.</summary>
    public static IReadOnlyList<(int Offset, int Length)> Find(string text, string? query, bool matchCase)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrEmpty(query)) return Array.Empty<(int, int)>();

        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var results = new List<(int, int)>();
        int start = 0;
        while (start <= text.Length - query.Length)
        {
            int idx = text.IndexOf(query, start, cmp);
            if (idx < 0) break;
            results.Add((idx, query.Length));
            start = idx + query.Length;
        }
        return results;
    }

    /// <summary>Replaces all occurrences of <paramref name="query"/> in <paramref name="text"/>.</summary>
    public static string ReplaceAll(string text, string query, string replacement, bool matchCase)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(replacement);
        if (query.Length == 0) return text;

        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var sb = new System.Text.StringBuilder();
        int last = 0;
        while (last <= text.Length)
        {
            int idx = last <= text.Length - query.Length
                ? text.IndexOf(query, last, cmp)
                : -1;
            if (idx < 0)
            {
                sb.Append(text, last, text.Length - last);
                break;
            }
            sb.Append(text, last, idx - last).Append(replacement);
            last = idx + query.Length;
        }
        return sb.ToString();
    }

    /// <summary>Replaces a single match at <paramref name="offset"/> with <paramref name="replacement"/>.</summary>
    public static string ReplaceSingle(string text, int offset, int length, string replacement)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(replacement);
        if (offset < 0 || offset > text.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (offset + length > text.Length) throw new ArgumentOutOfRangeException(nameof(length));
        return string.Concat(text.AsSpan(0, offset), replacement, text.AsSpan(offset + length));
    }
}
