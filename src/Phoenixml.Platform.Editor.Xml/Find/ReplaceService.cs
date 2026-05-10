using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Editor.Xml.Editing;

namespace Phoenixml.Platform.Editor.Xml.Find;

/// <summary>
/// Builds <see cref="IEditorCommand"/> instances that perform substring replacement
/// on a list of matched nodes. Intended to consume the output of <see cref="FindService"/>
/// and produce a ready-to-execute command list for a Replace or Replace All gesture.
/// </summary>
/// <remarks>
/// <para>
/// Each node in the match list is inspected at call time (the document's current state).
/// Matches whose substring is no longer present (stale matches) are silently filtered out.
/// </para>
/// <para>
/// Node-kind dispatch:
/// <list type="bullet">
///   <item><description>
///     <see cref="XdmElement"/> — substring replace on <c>LocalName</c> →
///     emits a <see cref="RenameElementCommand"/> when the name changes.
///   </description></item>
///   <item><description>
///     <see cref="XdmAttribute"/> — substring replace on <c>LocalName</c> and <c>Value</c>
///     independently. Emits a <see cref="RenameAttributeCommand"/> and/or a
///     <see cref="SetAttributeValueCommand"/> for whichever parts change.
///   </description></item>
///   <item><description>
///     <see cref="XdmText"/> — substring replace on <c>Value</c> →
///     emits a <see cref="SetTextContentCommand"/> against the text node's parent element.
///     Text nodes with a <see langword="null"/> parent are skipped defensively.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// The returned list is designed to be executed as a <see cref="CompoundCommand"/> for
/// Replace All, or applied individually for Replace Current.
/// </para>
/// </remarks>
public sealed class ReplaceService
{
    /// <summary>
    /// Builds replacement commands for the given match list.
    /// </summary>
    /// <param name="doc">
    /// The document to inspect and build commands for. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="matches">
    /// The list of <see cref="NodeId"/> values to process (typically from
    /// <see cref="FindService.Find"/>). Must not be <see langword="null"/>.
    /// </param>
    /// <param name="query">
    /// The substring to replace. Must not be <see langword="null"/>. Returns an empty list
    /// when empty.
    /// </param>
    /// <param name="replacement">
    /// The replacement string. May be empty (removal gesture). Must not be
    /// <see langword="null"/>.
    /// </param>
    /// <param name="matchCase">
    /// <see langword="true"/> for exact-case (<see cref="StringComparison.Ordinal"/>) matching;
    /// <see langword="false"/> for case-insensitive (<see cref="StringComparison.OrdinalIgnoreCase"/>)
    /// matching.
    /// </param>
    /// <returns>
    /// An <see cref="IReadOnlyList{T}"/> of commands. Never <see langword="null"/>; may be empty
    /// if no matches produce a change.
    /// </returns>
    public static IReadOnlyList<IEditorCommand> BuildReplacements(
        XmlDocument doc,
        IReadOnlyList<NodeId> matches,
        string query,
        string replacement,
        bool matchCase)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(replacement);

        if (string.IsNullOrEmpty(query)) return Array.Empty<IEditorCommand>();

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var commands = new List<IEditorCommand>();

        foreach (var id in matches)
        {
            var node = doc.Resolve(id);
            if (node is null) continue;

            switch (node)
            {
                case XdmElement el:
                {
                    var newLocal = ReplaceSubstring(el.LocalName, query, replacement, comparison);
                    if (newLocal != el.LocalName)
                        commands.Add(new RenameElementCommand(doc, id, newLocal));
                    break;
                }

                case XdmAttribute attr:
                {
                    var newAttrLocal = ReplaceSubstring(attr.LocalName, query, replacement, comparison);
                    var newAttrValue = ReplaceSubstring(attr.Value, query, replacement, comparison);

                    // Rename first — the rename keeps the NodeId stable, so the subsequent
                    // set-value command targeting the same NodeId remains valid.
                    if (newAttrLocal != attr.LocalName)
                        commands.Add(new RenameAttributeCommand(doc, id, newAttrLocal));
                    if (newAttrValue != attr.Value)
                        commands.Add(new SetAttributeValueCommand(doc, id, newAttrValue));
                    break;
                }

                case XdmText t:
                {
                    var newText = ReplaceSubstring(t.Value, query, replacement, comparison);
                    if (newText != t.Value)
                    {
                        // SetTextContentCommand operates on the parent element, not the
                        // text node itself. Skip orphaned text nodes defensively.
                        if (t.Parent is NodeId parentId)
                            commands.Add(new SetTextContentCommand(doc, parentId, newText));
                    }
                    break;
                }
            }
        }

        return commands;
    }

    /// <summary>
    /// Replaces all occurrences of <paramref name="query"/> in <paramref name="source"/>
    /// using the specified <paramref name="comparison"/>.
    /// </summary>
    private static string ReplaceSubstring(
        string source,
        string query,
        string replacement,
        StringComparison comparison)
    {
        if (source.Length == 0 || query.Length == 0) return source;

        // For Ordinal comparisons the BCL overload is sufficient and avoids allocation.
        if (comparison == StringComparison.Ordinal)
            return source.Replace(query, replacement, StringComparison.Ordinal);

        // For OrdinalIgnoreCase the BCL string.Replace(string, string, StringComparison)
        // overload is available on .NET 5+, so we use it directly.
        return source.Replace(query, replacement, comparison);
    }
}
