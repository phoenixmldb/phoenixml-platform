using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Tests.Fixtures;
using Xunit;

namespace Phoenixml.Platform.Tests.Documents;

public class DocumentTests
{
    [Fact]
    public void ChangedFiresWithEditKindAfterCommandExecuted()
    {
        var doc = new TestDocument();
        DocumentChange? received = null;
        doc.Changed += c => received = c;

        doc.Undo.Execute(new TestCommand { Description = "my edit" });

        Assert.NotNull(received);
        Assert.Equal(DocumentChangeKind.Edit, received.Kind);
        Assert.Equal("my edit", received.Description);
    }

    [Fact]
    public void ChangedFiresWithUndoAppliedKindAfterUndo()
    {
        var doc = new TestDocument();
        doc.Undo.Execute(new TestCommand { Description = "edit" });
        DocumentChange? received = null;
        doc.Changed += c => received = c;

        doc.Undo.Undo();

        Assert.NotNull(received);
        Assert.Equal(DocumentChangeKind.UndoApplied, received.Kind);
    }

    [Fact]
    public void ChangedFiresWithRedoAppliedKindAfterRedo()
    {
        var doc = new TestDocument();
        doc.Undo.Execute(new TestCommand { Description = "edit" });
        doc.Undo.Undo();
        DocumentChange? received = null;
        doc.Changed += c => received = c;

        doc.Undo.Redo();

        Assert.NotNull(received);
        Assert.Equal(DocumentChangeKind.RedoApplied, received.Kind);
    }

    [Fact]
    public async Task MarkCleanResetsDirtyAndRaisesSavedChange()
    {
        var doc = new TestDocument();
        doc.Undo.Execute(new TestCommand { Description = "edit" });
        Assert.True(doc.IsDirty);

        DocumentChange? received = null;
        doc.Changed += c => received = c;

        await doc.SaveAsync(CancellationToken.None);

        Assert.False(doc.IsDirty);
        Assert.NotNull(received);
        Assert.Equal(DocumentChangeKind.Saved, received.Kind);
    }

    [Fact]
    public void DocumentChangeDocumentIdMatchesDocumentId()
    {
        var doc = new TestDocument();
        DocumentChange? received = null;
        doc.Changed += c => received = c;

        doc.Undo.Execute(new TestCommand { Description = "edit" });

        Assert.NotNull(received);
        Assert.Equal(doc.Id, received.DocumentId);
    }

    [Fact]
    public void IsDirtyStartsFalseOnNewDocument()
    {
        var doc = new TestDocument();
        Assert.False(doc.IsDirty);
    }
}
