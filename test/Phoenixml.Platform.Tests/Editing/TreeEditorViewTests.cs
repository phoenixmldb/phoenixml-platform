using Phoenixml.Platform.Abstractions.Editing;
using Xunit;

namespace Phoenixml.Platform.Tests.Editing;

// Minimal stub ITreeNode.
file sealed class StubNode(string id, string label, bool hasChildren = false) : ITreeNode
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public string? IconKey { get; }
    public bool HasChildren { get; } = hasChildren;
    public string Value { get; } = string.Empty;
    public string EditableName => Label;
}

// Minimal stub ITreeNodeProvider.
file sealed class StubProvider : ITreeNodeProvider
{
    private readonly List<ITreeNode> _roots;
    private readonly Dictionary<string, IReadOnlyList<ITreeNode>> _children;

#pragma warning disable CA1003 // intentional Action pattern
    public event Action? Changed;
#pragma warning restore CA1003

    public IReadOnlyList<ITreeNode> Roots => _roots;

    public StubProvider(IEnumerable<ITreeNode> roots,
                        Dictionary<string, IReadOnlyList<ITreeNode>>? children = null)
    {
        _roots = roots.ToList();
        _children = children ?? [];
    }

    public IReadOnlyList<ITreeNode> GetChildren(ITreeNode node)
        => _children.TryGetValue(node.Id, out var ch) ? ch : [];

    public void RaiseChanged() => Changed?.Invoke();
}

public class TreeEditorViewTests
{
    [Fact]
    public void SetProviderNullClearsProvider()
    {
        var view = new Phoenixml.Platform.Editing.TreeEditorView();
        var provider = new StubProvider([new StubNode("a", "A")]);
        view.SetProvider(provider);
        view.SetProvider(null);
        Assert.Null(view.Provider);
    }

    [Fact]
    public void SetProviderStoresProvider()
    {
        var view = new Phoenixml.Platform.Editing.TreeEditorView();
        var provider = new StubProvider([new StubNode("a", "A")]);
        view.SetProvider(provider);
        Assert.Same(provider, view.Provider);
    }

    [Fact]
    public void SetProviderDoesNotThrow()
    {
        // Smoke test: wire up a non-null provider without error.
        var view = new Phoenixml.Platform.Editing.TreeEditorView();
        var provider = new StubProvider(
            [new StubNode("root", "Root", hasChildren: true)],
            new Dictionary<string, IReadOnlyList<ITreeNode>>
            {
                ["root"] = [new StubNode("child1", "Child 1"), new StubNode("child2", "Child 2")]
            });
        view.SetProvider(provider); // must not throw
    }

    [Fact]
    public void ProviderChangedEventDoesNotThrow()
    {
        var view = new Phoenixml.Platform.Editing.TreeEditorView();
        var provider = new StubProvider([new StubNode("a", "A")]);
        view.SetProvider(provider);
        provider.RaiseChanged(); // must not throw
    }

    [Fact]
    public void SetProviderNullFromTheStartDoesNotThrow()
    {
        var view = new Phoenixml.Platform.Editing.TreeEditorView();
        view.SetProvider(null);
        Assert.Null(view.Provider);
    }

    [Fact]
    public void InitialSelectedNodeIsNull()
    {
        var view = new Phoenixml.Platform.Editing.TreeEditorView();
        Assert.Null(view.SelectedNode);
    }
}
