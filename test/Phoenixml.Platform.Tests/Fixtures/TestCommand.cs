using Phoenixml.Platform.Abstractions.Documents;

namespace Phoenixml.Platform.Tests.Fixtures;

internal sealed class TestCommand : IEditorCommand
{
    public string Description { get; init; } = "Test edit";
    public Action<IDocument>? OnApply { get; init; }
    public Action<IDocument>? OnRevert { get; init; }
    public Func<IEditorCommand, IEditorCommand?>? MergeStrategy { get; init; }

    public void Apply(IDocument document)
    {
        if (document is TestDocument td) td.AppliedOperations.Add($"apply:{Description}");
        OnApply?.Invoke(document);
    }

    public void Revert(IDocument document)
    {
        if (document is TestDocument td) td.AppliedOperations.Add($"revert:{Description}");
        OnRevert?.Invoke(document);
    }

    public bool TryMergeWith(IEditorCommand previous, out IEditorCommand merged)
    {
        if (MergeStrategy is null)
        {
            merged = this;
            return false;
        }
        var result = MergeStrategy(previous);
        if (result is null) { merged = this; return false; }
        merged = result;
        return true;
    }
}
