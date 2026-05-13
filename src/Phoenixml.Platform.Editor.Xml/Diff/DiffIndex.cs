using System.Collections.Generic;
using PhoenixmlDb.Core;

namespace Phoenixml.Platform.Editor.Xml.Diff;

/// <summary>Which side of a comparison this index represents.</summary>
public enum DiffSide
{
    /// <summary>The "before" / source document.</summary>
    Left,
    /// <summary>The "after" / target document.</summary>
    Right,
}

/// <summary>
/// Runtime lookup for diff-tinting in <c>NodeListView</c>. Parallel to <c>MatchIndex</c>
/// from Plan 15. Maps <see cref="NodeId"/> → <see cref="DiffKind"/> so the tree provider
/// can paint added/removed/changed rows with distinct background colors.
/// </summary>
public sealed class DiffIndex
{
    private readonly Dictionary<NodeId, DiffKind> _byId = new();

    /// <summary>Number of tinted nodes.</summary>
    public int Count => _byId.Count;

    /// <summary>Returns the diff kind for <paramref name="id"/>, or <see langword="null"/>.</summary>
    public DiffKind? GetKind(NodeId id) =>
        _byId.TryGetValue(id, out var k) ? k : null;

    /// <summary>Replaces the index contents.</summary>
    public void Reset(IReadOnlyDictionary<NodeId, DiffKind> entries)
    {
        System.ArgumentNullException.ThrowIfNull(entries);
        _byId.Clear();
        foreach (var (id, kind) in entries)
            _byId[id] = kind;
    }
}
