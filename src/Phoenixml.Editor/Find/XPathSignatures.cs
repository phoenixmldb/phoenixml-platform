using System;
using System.Collections.Generic;

namespace Phoenixml.Editor.Find;

/// <summary>
/// Curated map of XPath 3.1 <c>fn:*</c> function signatures (local name → signature string).
/// MVP: covers the most-common ~35 functions. Function names are case-sensitive per XPath.
/// </summary>
public static class XPathSignatures
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        ["abs"] = "abs($arg as xs:numeric?) as xs:numeric?",
        ["boolean"] = "boolean($arg as item()*) as xs:boolean",
        ["ceiling"] = "ceiling($arg as xs:numeric?) as xs:numeric?",
        ["concat"] = "concat($arg1, $arg2, …) as xs:string",
        ["contains"] = "contains($arg1 as xs:string?, $arg2 as xs:string?) as xs:boolean",
        ["count"] = "count($arg as item()*) as xs:integer",
        ["data"] = "data($arg as item()*) as xs:anyAtomicType*",
        ["distinct-values"] = "distinct-values($arg as xs:anyAtomicType*) as xs:anyAtomicType*",
        ["doc"] = "doc($uri as xs:string?) as document-node()?",
        ["empty"] = "empty($arg as item()*) as xs:boolean",
        ["ends-with"] = "ends-with($arg1 as xs:string?, $arg2 as xs:string?) as xs:boolean",
        ["exists"] = "exists($arg as item()*) as xs:boolean",
        ["false"] = "false() as xs:boolean",
        ["floor"] = "floor($arg as xs:numeric?) as xs:numeric?",
        ["head"] = "head($arg as item()*) as item()?",
        ["last"] = "last() as xs:integer",
        ["local-name"] = "local-name($arg as node()?) as xs:string",
        ["lower-case"] = "lower-case($arg as xs:string?) as xs:string",
        ["matches"] = "matches($input as xs:string?, $pattern as xs:string) as xs:boolean",
        ["max"] = "max($arg as xs:anyAtomicType*) as xs:anyAtomicType?",
        ["min"] = "min($arg as xs:anyAtomicType*) as xs:anyAtomicType?",
        ["name"] = "name($arg as node()?) as xs:string",
        ["namespace-uri"] = "namespace-uri($arg as node()?) as xs:anyURI",
        ["normalize-space"] = "normalize-space($arg as xs:string?) as xs:string",
        ["not"] = "not($arg as item()*) as xs:boolean",
        ["number"] = "number($arg as xs:anyAtomicType?) as xs:double",
        ["position"] = "position() as xs:integer",
        ["replace"] = "replace($input as xs:string?, $pattern as xs:string, $replacement as xs:string) as xs:string",
        ["round"] = "round($arg as xs:numeric?) as xs:numeric?",
        ["starts-with"] = "starts-with($arg1 as xs:string?, $arg2 as xs:string?) as xs:boolean",
        ["string"] = "string($arg as item()?) as xs:string",
        ["string-join"] = "string-join($arg as xs:anyAtomicType*, $sep as xs:string?) as xs:string",
        ["string-length"] = "string-length($arg as xs:string?) as xs:integer",
        ["substring"] = "substring($source as xs:string?, $start as xs:double, $length as xs:double?) as xs:string",
        ["sum"] = "sum($arg as xs:anyAtomicType*, $zero as xs:anyAtomicType?) as xs:anyAtomicType?",
        ["tokenize"] = "tokenize($input as xs:string?, $pattern as xs:string) as xs:string*",
        ["true"] = "true() as xs:boolean",
        ["upper-case"] = "upper-case($arg as xs:string?) as xs:string",
    };

    /// <summary>Returns the signature string for <paramref name="name"/>, or null.</summary>
    public static string? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Map.TryGetValue(name, out var sig) ? sig : null;
    }
}
