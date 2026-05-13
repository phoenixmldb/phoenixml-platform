using System;
using System.Collections.Generic;

namespace Phoenixml.Editor.Find;

/// <summary>
/// Per-session XPath query history. Most-recent first, deduplicated (re-submitting an
/// entry moves it to the front), capped at <see cref="Capacity"/>. Not persisted across
/// sessions in v0.13.
/// </summary>
public sealed class XPathHistory
{
    private readonly List<string> _entries = new();

    /// <summary>Initializes a new <see cref="XPathHistory"/> with the given capacity.</summary>
    public XPathHistory(int capacity = 50)
    {
        Capacity = capacity;
    }

    /// <summary>Maximum number of retained entries.</summary>
    public int Capacity { get; }

    /// <summary>Snapshot of entries, most-recent first.</summary>
    public IReadOnlyList<string> Entries => _entries;

    /// <summary>Appends to the front; dedupes; trims to capacity. Empty/whitespace is ignored.</summary>
    public void Record(string xpath)
    {
        ArgumentNullException.ThrowIfNull(xpath);
        if (string.IsNullOrWhiteSpace(xpath)) return;
        _entries.Remove(xpath);
        _entries.Insert(0, xpath);
        while (_entries.Count > Capacity)
            _entries.RemoveAt(_entries.Count - 1);
    }

    /// <summary>
    /// Seeds the history from a list (most-recent first). Replaces existing entries.
    /// Items beyond <see cref="Capacity"/> are dropped.
    /// </summary>
    public void LoadFrom(IReadOnlyList<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries.Clear();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            _entries.Add(entry);
            if (_entries.Count >= Capacity) break;
        }
    }
}
