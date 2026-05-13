using System;
using System.Collections.Generic;
using System.Linq;

namespace Phoenixml.Editor.Find;

/// <summary>
/// MVP autocompletion source for the XPath query bar. Hand-curated list of common
/// XPath 3.1 <c>fn:*</c> function names (local names only). Case-insensitive prefix match.
/// v0.13 has no signature / parameter / namespace-aware filtering — those are Plan 24.
/// </summary>
public static class XPathFunctionList
{
    private static readonly string[] Names =
    [
        "abs", "boolean", "ceiling", "codepoints-to-string", "compare", "concat",
        "contains", "count", "current-date", "current-dateTime", "current-time",
        "data", "deep-equal", "distinct-values", "doc", "doc-available", "empty",
        "ends-with", "error", "exists", "false", "filter", "floor", "fold-left",
        "fold-right", "for-each", "format-date", "format-dateTime", "format-number",
        "format-time", "head", "id", "idref", "implicit-timezone", "index-of",
        "last", "local-name", "lower-case", "matches", "max", "min", "name",
        "namespace-uri", "nilled", "node-name", "normalize-space", "not", "number",
        "parse-xml", "path", "position", "prefix-from-QName", "remove",
        "replace", "resolve-uri", "reverse", "root", "round", "round-half-to-even",
        "serialize", "starts-with", "string", "string-join", "string-length",
        "string-to-codepoints", "subsequence", "substring", "substring-after",
        "substring-before", "sum", "tail", "tokenize", "trace", "translate",
        "true", "unparsed-text", "upper-case", "year-from-date",
        "year-from-dateTime", "zero-or-one",
    ];

    /// <summary>Names whose local name starts with <paramref name="prefix"/> (case-insensitive). Empty prefix returns all.</summary>
    public static IReadOnlyList<string> Match(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Length == 0) return Names;
        return Names
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
