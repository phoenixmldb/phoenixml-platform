using System.Collections.Concurrent;
using System.IO;
using System.Xml;
using System.Xml.Schema;
using EditorXmlSchemaValidator = Phoenixml.Platform.Editor.Xml.Validation.XmlSchemaValidator;

namespace Phoenixml.Workbench.Validation;

/// <summary>
/// Caches compiled <see cref="XmlSchemaSet"/> instances keyed by absolute path and
/// last-modified-utc ticks.
/// </summary>
/// <remarks>
/// <para>
/// On a cache hit the cached instance is returned directly — no disk read. On a miss the
/// schema is loaded, compiled, stored, and returned.
/// </para>
/// <para>
/// The cache key incorporates <c>File.GetLastWriteTimeUtc</c> ticks so external XSD
/// edits during a session are picked up on the next access without any explicit invalidation.
/// </para>
/// <para>
/// There is a benign double-load race window: two concurrent calls that both miss the cache for
/// the same key may each load and store independently. The second store overwrites the first with
/// an equivalent value. The cost (one extra load) is negligible given validation is debounced.
/// </para>
/// </remarks>
public sealed class SchemaCache
{
    // Primary cache: keyed by (absolutePath, mtime-ticks).
    // On cache hit the instance is returned without any disk access.
    // An mtime change produces a new key → fresh load on the next access.
    private readonly ConcurrentDictionary<(string Path, long Ticks), XmlSchemaSet> _byKey = new();

    // Secondary index: last successfully loaded set keyed by absolute path alone.
    // Consulted when File.Exists returns false — covers the "file deleted between
    // validation passes" edge case without changing the primary semantics.
    private readonly ConcurrentDictionary<string, XmlSchemaSet> _byPath = new();

    /// <summary>
    /// Returns a compiled <see cref="XmlSchemaSet"/> for <paramref name="xsdPath"/>,
    /// loading from disk on first access and on mtime change; returning the cached
    /// instance on subsequent accesses with the same mtime.
    /// </summary>
    /// <param name="xsdPath">Absolute or relative path to the XSD file.</param>
    /// <returns>
    /// A compiled <see cref="XmlSchemaSet"/>, or <see langword="null"/> when the file is
    /// missing (and has never been loaded in this session), unreadable, or contains invalid
    /// schema content.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="xsdPath"/> is <see langword="null"/> or empty.
    /// </exception>
    public XmlSchemaSet? GetOrLoad(string xsdPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(xsdPath);

        var absolutePath = Path.GetFullPath(xsdPath);

        if (!File.Exists(absolutePath))
        {
            // File gone — serve the last known good entry if available.
            return _byPath.TryGetValue(absolutePath, out var stale) ? stale : null;
        }

        var ticks = File.GetLastWriteTimeUtc(absolutePath).Ticks;
        var key = (absolutePath, ticks);

        if (_byKey.TryGetValue(key, out var cached))
            return cached;

        XmlSchemaSet? loaded;
        try
        {
            loaded = EditorXmlSchemaValidator.LoadSchema(absolutePath);
        }
        catch (XmlSchemaException) { return null; }
        catch (XmlException) { return null; }
        catch (IOException) { return null; }

        _byKey[key] = loaded;
        _byPath[absolutePath] = loaded;
        return loaded;
    }

    /// <summary>
    /// Evicts all cached entries.
    /// </summary>
    public void Clear()
    {
        _byKey.Clear();
        _byPath.Clear();
    }
}
