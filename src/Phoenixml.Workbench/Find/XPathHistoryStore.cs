using System;
using System.Collections.Generic;
using System.IO;

namespace Phoenixml.Workbench.Find;

/// <summary>
/// On-disk persistence for <see cref="XPathHistory"/>. Plain-text, newline-delimited;
/// blank lines are ignored on load. Stored under
/// <c>~/.config/phoenixml/xpath-history.txt</c> by default.
/// </summary>
public static class XPathHistoryStore
{
    /// <summary>The default user-config path for the history file.</summary>
    public static string DefaultPath
    {
        get
        {
            var home = Environment.GetEnvironmentVariable("HOME")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "phoenixml", "xpath-history.txt");
        }
    }

    /// <summary>
    /// Loads entries from <paramref name="path"/>. Returns an empty list when the file
    /// is missing; blank lines are skipped.
    /// </summary>
    public static IReadOnlyList<string> Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path)) return Array.Empty<string>();
        var result = new List<string>();
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                result.Add(trimmed);
        }
        return result;
    }

    /// <summary>Writes <paramref name="entries"/> to <paramref name="path"/>, one per line.</summary>
    public static void Save(string path, IReadOnlyList<string> entries)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(entries);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllLines(path, entries);
    }
}
