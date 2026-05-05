using System.Collections.Generic;
using System.IO;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Parsing;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Documents;
using PlatformDocumentId = Phoenixml.Platform.Abstractions.Documents.DocumentId;
using XdmDocumentId = PhoenixmlDb.Core.DocumentId;

namespace Phoenixml.Platform.Editor.Xml;

/// <summary>
/// XML document backed by PhoenixmlDb.Core's XDM model. Constructed by parsing a
/// source string or stream; serializable back to XML via Save.
/// </summary>
public sealed class XmlDocument : Document
{
    private readonly XdmDocument _root;
    private readonly Dictionary<NodeId, XdmNode> _byId;
    private readonly Dictionary<NamespaceId, string> _namespaceById;

    /// <summary>
    /// Initializes a new <see cref="XmlDocument"/> from a parsed result.
    /// </summary>
    /// <param name="id">The stable platform document identifier.</param>
    /// <param name="source">Source URI, or <see langword="null"/> for new unsaved documents.</param>
    /// <param name="displayName">Display name shown in tabs and title bars.</param>
    /// <param name="parseResult">The XDM parse result from <c>XmlDocumentParser</c>.</param>
    /// <param name="namespaceById">Namespace URI lookup keyed by <see cref="NamespaceId"/>.</param>
    public XmlDocument(
        PlatformDocumentId id,
        Uri? source,
        string displayName,
        ParseResult parseResult,
        Dictionary<NamespaceId, string> namespaceById)
        : base(id, source, displayName, DocumentKind.Xml)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(namespaceById);
        _root = parseResult.Document;
        _byId = parseResult.Nodes.ToDictionary(n => n.Id, n => n);
        _namespaceById = namespaceById;
        TreeNodes = new Tree.XmlTreeNodeProvider(this);
    }

    /// <summary>The XDM document root. Mutating XDM directly is not supported in v0.1; use commands via the document's <c>Undo</c> stack.</summary>
    public XdmDocument Root => _root;

    /// <summary>Resolves a NodeId to its XdmNode. Returns null if not found.</summary>
    public XdmNode? Resolve(NodeId id) => _byId.TryGetValue(id, out var n) ? n : null;

    /// <summary>Tree-node provider for binding to <c>TreeEditorView</c>.</summary>
    public Tree.XmlTreeNodeProvider TreeNodes { get; }

    /// <summary>Loads an XML document from <paramref name="path"/>.</summary>
    /// <param name="path">Absolute file path to the XML document.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A loaded <see cref="XmlDocument"/> instance.</returns>
    public static async ValueTask<XmlDocument> LoadAsync(string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);
        var xml = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var (result, nsTable) = ParseXml(xml, new Uri(path).ToString());
        var displayName = Path.GetFileName(path);
        return new XmlDocument(PlatformDocumentId.New(), new Uri(path), displayName, result, nsTable);
    }

    /// <inheritdoc/>
    public override async ValueTask SaveAsync(CancellationToken ct)
    {
        if (Source is null)
            throw new InvalidOperationException("Cannot save a document with no Source URI; use SaveAsAsync.");

        var xml = SerializeToString();
        var path = Source.LocalPath;
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, xml, ct).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
        MarkClean();
    }

    /// <summary>Saves the document to a different location and updates the Source URI.</summary>
    /// <param name="target">Target file URI.</param>
    /// <param name="ct">Cancellation token.</param>
    public async ValueTask SaveAsAsync(Uri target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        var xml = SerializeToString();
        var path = target.LocalPath;
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, xml, ct).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
        Source = target;
        MarkClean();
    }

    /// <summary>Serializes the XDM document back to an XML string.</summary>
    internal string SerializeToString()
    {
        var serializer = new XmlSerializer(
            nodeResolver: id => _byId.TryGetValue(id, out var n) ? n : null,
            namespaceResolver: nsId => _namespaceById.TryGetValue(nsId, out var uri) ? uri : null);
        return serializer.Serialize(_root);
    }

    /// <summary>
    /// Parses XML text into an XDM tree, returning the result and namespace lookup table.
    /// </summary>
    /// <param name="xml">The XML text to parse.</param>
    /// <param name="documentUri">Optional document URI.</param>
    /// <returns>The parse result and a dictionary mapping <see cref="NamespaceId"/> to URI string.</returns>
    internal static (ParseResult Result, Dictionary<NamespaceId, string> NamespaceById) ParseXml(
        string xml, string? documentUri = null)
    {
        // Build a dictionary-backed namespace intern table. NamespaceId.None (0) means no namespace.
        // Pre-seed the well-known IDs that PhoenixmlDb.Core reserves.
        var nsUriToId = new Dictionary<string, NamespaceId>(StringComparer.Ordinal)
        {
            { "http://www.w3.org/XML/1998/namespace", NamespaceId.Xml },
            { "http://www.w3.org/2000/xmlns/", NamespaceId.Xmlns },
            { "http://www.w3.org/2001/XMLSchema", NamespaceId.Xsd },
            { "http://www.w3.org/2001/XMLSchema-instance", NamespaceId.Xsi },
            { "http://www.w3.org/2005/xpath-functions", NamespaceId.Fn },
            { "http://www.w3.org/2005/xpath-functions/map", NamespaceId.Map },
            { "http://www.w3.org/2005/xpath-functions/array", NamespaceId.Array },
            { "http://www.w3.org/2005/xpath-functions/math", NamespaceId.Math },
        };
        var nsIdToUri = new Dictionary<NamespaceId, string>
        {
            { NamespaceId.Xml, "http://www.w3.org/XML/1998/namespace" },
            { NamespaceId.Xmlns, "http://www.w3.org/2000/xmlns/" },
            { NamespaceId.Xsd, "http://www.w3.org/2001/XMLSchema" },
            { NamespaceId.Xsi, "http://www.w3.org/2001/XMLSchema-instance" },
            { NamespaceId.Fn, "http://www.w3.org/2005/xpath-functions" },
            { NamespaceId.Map, "http://www.w3.org/2005/xpath-functions/map" },
            { NamespaceId.Array, "http://www.w3.org/2005/xpath-functions/array" },
            { NamespaceId.Math, "http://www.w3.org/2005/xpath-functions/math" },
        };
        uint nextNsId = NamespaceId.FirstUserNamespaceId;

        NamespaceId InternNamespace(string uri)
        {
            if (string.IsNullOrEmpty(uri))
                return NamespaceId.None;
            if (nsUriToId.TryGetValue(uri, out var existing))
                return existing;
            var id = new NamespaceId(nextNsId++);
            nsUriToId[uri] = id;
            nsIdToUri[id] = uri;
            return id;
        }

        var parser = new XmlDocumentParser(
            documentId: new XdmDocumentId(1),
            startNodeId: new NodeId(1),
            namespaceResolver: InternNamespace);

        var result = parser.Parse(xml, documentUri);
        return (result, nsIdToUri);
    }
}
