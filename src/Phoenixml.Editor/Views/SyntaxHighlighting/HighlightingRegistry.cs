using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using Phoenixml.Platform.Abstractions.Documents;

namespace Phoenixml.Editor.Views.SyntaxHighlighting;

/// <summary>
/// Resolves an <see cref="IHighlightingDefinition"/> for a given <see cref="DocumentKind"/>.
/// XQuery loads from an embedded <c>.xshd</c>; XSLT reuses AvaloniaEdit's built-in XML
/// highlighting. Results are cached.
/// </summary>
public sealed class HighlightingRegistry
{
    private readonly Dictionary<DocumentKind, IHighlightingDefinition?> _cache = new();

    /// <summary>Returns the highlighting definition for <paramref name="kind"/>, or null.</summary>
    public IHighlightingDefinition? Resolve(DocumentKind kind)
    {
        if (_cache.TryGetValue(kind, out var cached)) return cached;
        var def = LoadFor(kind);
        _cache[kind] = def;
        return def;
    }

    private static IHighlightingDefinition? LoadFor(DocumentKind kind) => kind switch
    {
        DocumentKind.XQuery => LoadEmbedded("XQuery.xshd"),
        DocumentKind.Xslt => SafeGetBuiltin("XML"),
        _ => null,
    };

    private static IHighlightingDefinition? SafeGetBuiltin(string name)
    {
        try { return HighlightingManager.Instance.GetDefinition(name); }
        catch (System.InvalidOperationException) { return null; }
    }

    private static IHighlightingDefinition? LoadEmbedded(string name)
    {
        var asm = typeof(HighlightingRegistry).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(name, StringComparison.Ordinal));
        if (resourceName is null) return null;

        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) return null;

        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
