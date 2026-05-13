using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Phoenixml.Platform.Editing;
using Phoenixml.Platform.Editor.Xml;
using Phoenixml.Platform.Editor.Xml.Diff;
using Phoenixml.Platform.Editor.Xml.Tree;

namespace Phoenixml.Editor.Compare;

/// <summary>
/// Modal-style window showing two documents side-by-side with diff-tinted rows.
/// Scrolling either pane scrolls the other to the same vertical offset.
/// Read-only — find / XPath / edit interactions on each pane are intentionally not wired here.
/// </summary>
public sealed partial class CompareDocsWindow : Window
{
    /// <summary>Initializes a new <see cref="CompareDocsWindow"/>. Call <see cref="Load"/> before Show.</summary>
    public CompareDocsWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Loads two documents, computes their diff, builds per-side <see cref="DiffIndex"/>es,
    /// and binds the panes. Synced scrolling is wired after the visual tree attaches.
    /// </summary>
    public void Load(XmlDocument left, XmlDocument right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var diff = XmlDocumentDiff.Compute(left, right);
        var header = this.FindControl<TextBlock>("HeaderText");
        if (header is not null)
            header.Text = $"{left.DisplayName} ↔ {right.DisplayName} — {diff.Changes.Count} difference(s)";

        var leftIdx = DiffIndexBuilder.Build(left, diff, DiffSide.Left);
        var rightIdx = DiffIndexBuilder.Build(right, diff, DiffSide.Right);

        var leftProvider = (XmlTreeNodeProvider)left.TreeNodes;
        var rightProvider = (XmlTreeNodeProvider)right.TreeNodes;
        leftProvider.DiffIndex = leftIdx;
        rightProvider.DiffIndex = rightIdx;

        var leftPane = this.FindControl<NodeListView>("LeftPane")!;
        var rightPane = this.FindControl<NodeListView>("RightPane")!;
        leftPane.SetProvider(leftProvider);
        rightPane.SetProvider(rightProvider);

        // Wire scroll sync after the window is shown — ListBox inner Scroll is null
        // until the template is applied.
        Opened += (_, _) => WireSyncedScroll(leftPane, rightPane);
    }

    private static void WireSyncedScroll(NodeListView a, NodeListView b)
    {
        var aHost = a.FindControl<ListBox>("RowsHost");
        var bHost = b.FindControl<ListBox>("RowsHost");
        if (aHost is null || bHost is null) return;
        if (aHost.Scroll is null || bHost.Scroll is null) return;

        var aScroll = aHost.Scroll;
        var bScroll = bHost.Scroll;
        var muting = false;

        aHost.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) =>
        {
            if (muting) return;
            muting = true;
            try { bScroll.Offset = bScroll.Offset.WithY(aScroll.Offset.Y); }
            finally { muting = false; }
        });
        bHost.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) =>
        {
            if (muting) return;
            muting = true;
            try { aScroll.Offset = aScroll.Offset.WithY(bScroll.Offset.Y); }
            finally { muting = false; }
        });
    }
}
