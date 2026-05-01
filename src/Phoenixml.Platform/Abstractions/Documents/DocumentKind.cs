namespace Phoenixml.Platform.Abstractions.Documents;

/// <summary>Identifies the vocabulary or file type of a document.</summary>
public enum DocumentKind
{
    /// <summary>The kind is not known or not set.</summary>
    Unknown,
    /// <summary>An XML document.</summary>
    Xml,
    /// <summary>An XML Schema Definition document.</summary>
    Xsd,
    /// <summary>An XSLT stylesheet.</summary>
    Xslt,
    /// <summary>An XQuery module.</summary>
    XQuery,
    /// <summary>A JSON document.</summary>
    Json,
    /// <summary>An XProc pipeline document.</summary>
    Pipeline,
}
