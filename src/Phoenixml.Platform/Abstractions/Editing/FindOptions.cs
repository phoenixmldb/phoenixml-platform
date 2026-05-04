namespace Phoenixml.Platform.Abstractions.Editing;

/// <summary>Options for <c>FindReplaceView</c>'s find/replace operations.</summary>
public sealed record FindOptions
{
    /// <summary>Text to search for.</summary>
    public string SearchText { get; init; } = string.Empty;

    /// <summary>Replacement text. Used only by replace operations.</summary>
    public string ReplaceText { get; init; } = string.Empty;

    /// <summary>When true, the search is case-sensitive.</summary>
    public bool MatchCase { get; init; }

    /// <summary>When true, only whole-word matches are returned.</summary>
    public bool MatchWholeWord { get; init; }

    /// <summary>When true, <see cref="SearchText"/> is interpreted as a regular expression.</summary>
    public bool UseRegex { get; init; }

    /// <summary>When true, <see cref="SearchText"/> is interpreted as an XPath expression.</summary>
    public bool UseXPath { get; init; }

    /// <summary>Scope over which the search is performed.</summary>
    public FindScope Scope { get; init; } = FindScope.CurrentDocument;
}

/// <summary>Scope for a find/replace operation.</summary>
public enum FindScope
{
    /// <summary>Search within the currently active document only.</summary>
    CurrentDocument,

    /// <summary>Search across all open documents.</summary>
    AllOpenDocuments,

    /// <summary>Search across the connected collection (database/folder).</summary>
    ConnectedCollection
}
