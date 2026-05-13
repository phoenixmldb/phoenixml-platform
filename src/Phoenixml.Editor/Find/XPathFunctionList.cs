using System;
using System.Collections.Generic;

namespace Phoenixml.Editor.Find;

/// <summary>
/// Autocompletion source for the XPath query bar. Hand-curated lists of XPath 3.1
/// function names grouped by namespace prefix. Case-insensitive prefix match.
/// Routes by namespace prefix in the input: <c>map:</c>, <c>array:</c>, <c>xs:</c>
/// select the corresponding bucket; unprefixed input filters <c>fn:</c> functions.
/// </summary>
public static class XPathFunctionList
{
    private static readonly string[] FnNames =
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

    private static readonly string[] MapNames =
    [
        "map:contains", "map:entry", "map:find", "map:for-each", "map:get",
        "map:keys", "map:merge", "map:put", "map:remove", "map:size",
    ];

    private static readonly string[] ArrayNames =
    [
        "array:append", "array:filter", "array:flatten", "array:fold-left",
        "array:fold-right", "array:for-each", "array:for-each-pair", "array:get",
        "array:head", "array:insert-before", "array:join", "array:put",
        "array:remove", "array:reverse", "array:size", "array:sort", "array:subarray",
        "array:tail",
    ];

    private static readonly string[] XsNames =
    [
        "xs:anyAtomicType", "xs:anyURI", "xs:base64Binary", "xs:boolean", "xs:byte",
        "xs:date", "xs:dateTime", "xs:dayTimeDuration", "xs:decimal", "xs:double",
        "xs:duration", "xs:float", "xs:gDay", "xs:gMonth", "xs:gMonthDay", "xs:gYear",
        "xs:gYearMonth", "xs:hexBinary", "xs:int", "xs:integer", "xs:language",
        "xs:long", "xs:Name", "xs:NCName", "xs:negativeInteger", "xs:nonNegativeInteger",
        "xs:nonPositiveInteger", "xs:normalizedString", "xs:positiveInteger",
        "xs:QName", "xs:short", "xs:string", "xs:time", "xs:token", "xs:unsignedByte",
        "xs:unsignedInt", "xs:unsignedLong", "xs:unsignedShort", "xs:yearMonthDuration",
    ];

    /// <summary>Returns names matching <paramref name="prefix"/>. Routes by namespace prefix.</summary>
    public static IReadOnlyList<string> Match(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.StartsWith("map:", StringComparison.Ordinal))
            return Filter(MapNames, prefix);
        if (prefix.StartsWith("array:", StringComparison.Ordinal))
            return Filter(ArrayNames, prefix);
        if (prefix.StartsWith("xs:", StringComparison.Ordinal))
            return Filter(XsNames, prefix);
        return Filter(FnNames, prefix);
    }

    private static IReadOnlyList<string> Filter(string[] source, string prefix)
    {
        if (prefix.Length == 0) return source;
        var list = new List<string>();
        foreach (var n in source)
        {
            if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                list.Add(n);
        }
        return list;
    }
}
