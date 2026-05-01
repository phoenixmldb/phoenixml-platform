using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Documents;

namespace Phoenixml.Platform.Tests.Fixtures;

internal sealed class TestDocument : Document
{
    private bool _blockClose;

    public List<string> AppliedOperations { get; } = new();

    public TestDocument()
        : base(DocumentId.New(), null, "test", DocumentKind.Unknown) { }

    public void BlockClose() => _blockClose = true;

    public override ValueTask SaveAsync(CancellationToken ct)
    {
        MarkClean();
        return default;
    }

    public override ValueTask<bool> CanCloseAsync(CancellationToken ct) =>
        ValueTask.FromResult(!_blockClose);
}
