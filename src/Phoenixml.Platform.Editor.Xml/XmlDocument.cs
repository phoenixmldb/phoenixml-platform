using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
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

        Compact();
        var xml = SerializeToString();
        var path = Source.LocalPath;
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, xml, ct).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
        MarkClean();
    }

    /// <summary>
    /// Resets dirty state without persisting. Use when the editor is discarding changes
    /// (e.g. Discard All on quit) so the subsequent close does not re-prompt.
    /// </summary>
    public new void MarkClean() => base.MarkClean();

    /// <summary>Saves the document to a different location and updates the Source URI.</summary>
    /// <param name="target">Target file URI.</param>
    /// <param name="ct">Cancellation token.</param>
    public async ValueTask SaveAsAsync(Uri target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        Compact();
        var xml = SerializeToString();
        var path = target.LocalPath;
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, xml, ct).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
        Source = target;
        MarkClean();
    }

    /// <summary>
    /// Mark-and-sweep eviction of orphan NodeIds in <see cref="_byId"/>. Marks every NodeId
    /// reachable from the document root, plus every NodeId referenced by commands on the
    /// undo and redo stacks (so undo/redo across save still works). Sweeps everything else.
    /// Called automatically before serialization in <see cref="SaveAsync"/> and <see cref="SaveAsAsync"/>.
    /// </summary>
    internal void Compact()
    {
        var alive = new HashSet<NodeId>();

        // Mark phase 1: BFS from the document root through Children + Attributes.
        var stack = new Stack<NodeId>();
        alive.Add(_root.Id);
        foreach (var childId in _root.Children)
            stack.Push(childId);

        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!alive.Add(id)) continue;
            if (!_byId.TryGetValue(id, out var node)) continue;

            if (node is XdmElement e)
            {
                foreach (var a in e.Attributes) stack.Push(a);
                foreach (var c in e.Children) stack.Push(c);
            }
        }

        // Mark phase 2: pin NodeIds referenced by commands on the undo and redo stacks.
        foreach (var cmd in Undo.UndoCommands)
            foreach (var id in cmd.ReferencedNodeIds)
                alive.Add(id);
        foreach (var cmd in Undo.RedoCommands)
            foreach (var id in cmd.ReferencedNodeIds)
                alive.Add(id);

        // Sweep: remove every entry not marked alive.
        var toRemove = new List<NodeId>();
        foreach (var id in _byId.Keys)
            if (!alive.Contains(id)) toRemove.Add(id);
        foreach (var id in toRemove) _byId.Remove(id);
    }

    // Counter used by AllocateNodeId. Null until the first allocation (lazy init from _byId).
    private ulong? _nextNodeIdValue;

    /// <summary>
    /// Allocates a fresh <see cref="NodeId"/> that is guaranteed not to collide with any
    /// id already present in <c>_byId</c>. On the first call the counter is seeded from
    /// <c>max(existing ids) + 1</c>; subsequent calls increment monotonically.
    /// </summary>
    /// <returns>A new <see cref="NodeId"/> not yet present in this document.</returns>
    internal NodeId AllocateNodeId()
    {
        if (_nextNodeIdValue is null)
        {
            ulong max = 0;
            foreach (var id in _byId.Keys)
                if (id.Value > max) max = id.Value;
            _nextNodeIdValue = max + 1;
        }
        var allocated = new NodeId(_nextNodeIdValue.Value);
        _nextNodeIdValue = _nextNodeIdValue.Value + 1;
        return allocated;
    }

    /// <summary>
    /// Adds <paramref name="node"/> to the resolver dictionary under its <see cref="XdmNode.Id"/>.
    /// Use this when inserting a brand-new node (e.g. a text child allocated via
    /// <see cref="AllocateNodeId"/>). For replacing an existing entry, use
    /// <see cref="ReplaceNode"/> instead.
    /// </summary>
    /// <param name="node">The node to add. Its <see cref="XdmNode.Id"/> must not already be present.</param>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A node with the same id already exists; use <see cref="ReplaceNode"/> for swaps.</exception>
    internal void AddNode(XdmNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (_byId.ContainsKey(node.Id))
            throw new InvalidOperationException(
                $"A node with id {node.Id} is already present; use ReplaceNode for swap.");
        _byId[node.Id] = node;
    }

    /// <summary>
    /// Replaces the node behind <paramref name="id"/> with <paramref name="replacement"/>.
    /// XdmNode types are immutable; "edits" happen by constructing a new node with the
    /// changed properties and swapping it into the resolver dictionary. The NodeId is
    /// preserved so parent Children lists (which reference by id) remain valid.
    /// </summary>
    /// <param name="id">The NodeId of the node to replace.</param>
    /// <param name="replacement">The replacement node; its <see cref="XdmNode.Id"/> must equal <paramref name="id"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="replacement"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="replacement"/>'s Id does not match <paramref name="id"/>.</exception>
    /// <exception cref="InvalidOperationException">No node with <paramref name="id"/> exists in this document.</exception>
    internal void ReplaceNode(NodeId id, XdmNode replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.Id != id)
            throw new ArgumentException(
                $"replacement.Id ({replacement.Id}) must equal the id being replaced ({id}).",
                nameof(replacement));
        if (!_byId.ContainsKey(id))
            throw new InvalidOperationException($"No node with id {id} in this document.");
        _byId[id] = replacement;
    }

    /// <summary>
    /// Resolves a <see cref="NamespaceId"/> to its URI string.
    /// Returns <see langword="null"/> when the id is <see cref="NamespaceId.None"/> or unknown.
    /// </summary>
    /// <param name="id">The namespace identifier to resolve.</param>
    /// <returns>The namespace URI string, or <see langword="null"/> for no-namespace.</returns>
    internal string? ResolveNamespace(NamespaceId id)
        => id == NamespaceId.None ? null
            : _namespaceById.TryGetValue(id, out var uri) ? uri : null;

    /// <summary>Serializes the XDM document back to an XML string.</summary>
    internal string SerializeToString()
    {
        var serializer = new XmlSerializer(
            nodeResolver: id => _byId.TryGetValue(id, out var n) ? n : null,
            namespaceResolver: nsId => _namespaceById.TryGetValue(nsId, out var uri) ? uri : null);
        return serializer.Serialize(_root);
    }

    /// <summary>
    /// Parses an XML string into a subtree and adopts it into this document by allocating
    /// fresh <see cref="NodeId"/> values from this document's allocator and registering every
    /// parsed node via <see cref="AddNode"/>. Returns the root <see cref="XdmElement"/> of
    /// the subtree, ready to be spliced into any parent within this document.
    /// </summary>
    /// <param name="xml">Well-formed XML text whose document element becomes the subtree root.</param>
    /// <returns>The subtree's root <see cref="XdmElement"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="xml"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.Xml.XmlException"><paramref name="xml"/> is not well-formed XML.</exception>
    /// <exception cref="InvalidOperationException">The XML has no document element.</exception>
    internal XdmElement ParseSubtreeXml(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        // 1. Parse with a temporary allocator (any start id is fine — we renumber below).
        var (tempResult, tempNsById) = ParseXml(xml);

        var tempDocElementId = tempResult.Document.DocumentElement
            ?? throw new InvalidOperationException("The XML string has no document element.");

        // 2. Merge the parsed namespace table into this document's table.
        //    Build a mapping: parsed NamespaceId → this-doc NamespaceId.
        //    Well-known IDs are reused directly; user namespaces get a new entry if they
        //    aren't already in our table (looked up by URI).
        var nsIdRemap = new Dictionary<NamespaceId, NamespaceId>();

        // Seed well-known IDs — they map to themselves.
        foreach (var wellKnown in new[]
        {
            NamespaceId.None, NamespaceId.Xml, NamespaceId.Xmlns,
            NamespaceId.Xsd, NamespaceId.Xsi, NamespaceId.Fn,
        })
        {
            nsIdRemap[wellKnown] = wellKnown;
        }

        // Build a reverse URI → NamespaceId map for our current table.
        var ourUriToId = new Dictionary<string, NamespaceId>(StringComparer.Ordinal);
        foreach (var kvp in _namespaceById)
            ourUriToId[kvp.Value] = kvp.Key;

        // Find the max existing NamespaceId so we can allocate above it.
        uint nextNsId = NamespaceId.FirstUserNamespaceId;
        foreach (var id in _namespaceById.Keys)
            if (id.Value >= nextNsId) nextNsId = id.Value + 1;

        foreach (var kvp in tempNsById)
        {
            if (nsIdRemap.ContainsKey(kvp.Key)) continue; // already mapped (well-known)

            var uri = kvp.Value;
            if (ourUriToId.TryGetValue(uri, out var existing))
            {
                nsIdRemap[kvp.Key] = existing;
            }
            else
            {
                var newId = new NamespaceId(nextNsId++);
                _namespaceById[newId] = uri;
                ourUriToId[uri] = newId;
                nsIdRemap[kvp.Key] = newId;
            }
        }

        // 3. Assign fresh NodeIds to every parsed node (except the temp XdmDocument wrapper).
        //    Build a mapping: parsed NodeId → new NodeId.
        var nodeIdRemap = new Dictionary<NodeId, NodeId>();
        foreach (var node in tempResult.Nodes)
        {
            if (node is XdmDocument) continue; // drop the wrapper document node
            nodeIdRemap[node.Id] = AllocateNodeId();
        }

        // Helper to remap a nullable parent pointer (skipping the temp document node).
        NodeId? RemapParent(NodeId? oldParent)
        {
            if (oldParent is null) return null;
            if (!nodeIdRemap.TryGetValue(oldParent.Value, out var remapped)) return null; // doc wrapper
            return remapped;
        }

        // Helper to remap a list of NodeIds (children / attributes).
        IReadOnlyList<NodeId> RemapIds(IReadOnlyList<NodeId> ids)
        {
            if (ids.Count == 0) return ImmutableArray<NodeId>.Empty;
            var b = ImmutableArray.CreateBuilder<NodeId>(ids.Count);
            foreach (var id in ids) b.Add(nodeIdRemap[id]);
            return b.ToImmutable();
        }

        // Helper to remap namespace bindings on an element.
        IReadOnlyList<NamespaceBinding> RemapBindings(IReadOnlyList<NamespaceBinding> bindings)
        {
            if (bindings.Count == 0) return ImmutableArray<NamespaceBinding>.Empty;
            var b = ImmutableArray.CreateBuilder<NamespaceBinding>(bindings.Count);
            foreach (var nb in bindings)
                b.Add(new NamespaceBinding(nb.Prefix, nsIdRemap.TryGetValue(nb.Namespace, out var rns) ? rns : nb.Namespace));
            return b.ToImmutable();
        }

        // Helper to remap a single NamespaceId.
        NamespaceId RemapNs(NamespaceId ns)
            => nsIdRemap.TryGetValue(ns, out var r) ? r : ns;

        // 4. Rebuild each node with remapped IDs and add to this document's store.
        XdmElement? subtreeRoot = null;

        foreach (var node in tempResult.Nodes)
        {
            if (node is XdmDocument) continue;

            var newId = nodeIdRemap[node.Id];
            var newParent = RemapParent(node.Parent);

            XdmNode rebuilt = node switch
            {
                XdmElement el => new XdmElement
                {
                    Id = newId,
                    Document = _root.Document,
                    Namespace = RemapNs(el.Namespace),
                    Prefix = el.Prefix,
                    LocalName = el.LocalName,
                    Parent = newParent,
                    Attributes = RemapIds(el.Attributes),
                    Children = RemapIds(el.Children),
                    NamespaceDeclarations = RemapBindings(el.NamespaceDeclarations),
                    TypeAnnotation = el.TypeAnnotation,
                    IsIdContent = el.IsIdContent,
                },
                XdmAttribute attr => new XdmAttribute
                {
                    Id = newId,
                    Document = _root.Document,
                    Namespace = RemapNs(attr.Namespace),
                    Prefix = attr.Prefix,
                    LocalName = attr.LocalName,
                    Parent = newParent,
                    Value = attr.Value,
                    TypeAnnotation = attr.TypeAnnotation,
                    IsId = attr.IsId,
                    IsIdRef = attr.IsIdRef,
                },
                XdmText txt => new XdmText
                {
                    Id = newId,
                    Document = _root.Document,
                    Parent = newParent,
                    Value = txt.Value,
                },
                XdmComment cmt => new XdmComment
                {
                    Id = newId,
                    Document = _root.Document,
                    Parent = newParent,
                    Value = cmt.Value,
                },
                XdmProcessingInstruction pi => new XdmProcessingInstruction
                {
                    Id = newId,
                    Document = _root.Document,
                    Parent = newParent,
                    Target = pi.Target,
                    Value = pi.Value,
                },
                _ => throw new InvalidOperationException($"Unexpected node kind: {node.NodeKind}")
            };

            AddNode(rebuilt);

            // The document element of the temp parse result is the subtree root.
            if (node.Id == tempDocElementId)
                subtreeRoot = (XdmElement)rebuilt;
        }

        return subtreeRoot
            ?? throw new InvalidOperationException("Subtree root element was not found after re-numbering.");
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
