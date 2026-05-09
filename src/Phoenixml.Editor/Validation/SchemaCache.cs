using System.Collections.Concurrent;
using System.IO;
using System.Xml;
using System.Xml.Schema;
using EditorXmlSchemaValidator = Phoenixml.Platform.Editor.Xml.Validation.XmlSchemaValidator;

namespace Phoenixml.Editor.Validation;

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
internal sealed class SchemaCache
{
    private readonly ConcurrentDictionary<(string Path, long Ticks), XmlSchemaSet> _cache = new();

    /// <summary>
    /// Returns a compiled <see cref="XmlSchemaSet"/> for <paramref name="xsdPath"/>,
    /// loading from disk on first access and on mtime change; returning the cached
    /// instance on subsequent accesses with the same mtime.
    /// </summary>
    /// <param name="xsdPath">Absolute or relative path to the XSD file.</param>
    /// <returns>
    /// A compiled <see cref="XmlSchemaSet"/>, or <see langword="null"/> when the file is
    /// missing, unreadable, or contains invalid schema content.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="xsdPath"/> is <see langword="null"/> or empty.
    /// </exception>
    public XmlSchemaSet? GetOrLoad(string xsdPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(xsdPath);
        if (!File.Exists(xsdPath)) return null;

        var ticks = File.GetLastWriteTimeUtc(xsdPath).Ticks;
        var key = (Path.GetFullPath(xsdPath), ticks);

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        XmlSchemaSet? loaded;
        try
        {
            loaded = EditorXmlSchemaValidator.LoadSchema(xsdPath);
        }
        catch (XmlSchemaException) { return null; }
        catch (XmlException) { return null; }
        catch (IOException) { return null; }

        _cache[key] = loaded;
        return loaded;
    }

    /// <summary>
    /// Evicts all cached entries.
    /// </summary>
    public void Clear() => _cache.Clear();
}
