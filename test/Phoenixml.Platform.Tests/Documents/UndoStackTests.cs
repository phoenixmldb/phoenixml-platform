using Phoenixml.Platform.Tests.Fixtures;
using Xunit;

namespace Phoenixml.Platform.Tests.Documents;

public class UndoStackTests
{
    [Fact]
    public void ExecuteAppliesCommandAndPushesToUndoStack()
    {
        var doc = new TestDocument();
        var cmd = new TestCommand { Description = "edit1" };

        doc.Undo.Execute(cmd);

        Assert.Contains("apply:edit1", doc.AppliedOperations);
        Assert.True(doc.Undo.CanUndo);
        Assert.Equal("edit1", doc.Undo.NextUndoDescription);
    }

    [Fact]
    public void ExecuteClearsRedoStack()
    {
        var doc = new TestDocument();
        var cmd1 = new TestCommand { Description = "edit1" };
        var cmd2 = new TestCommand { Description = "edit2" };

        doc.Undo.Execute(cmd1);
        doc.Undo.Undo(); // moves cmd1 to redo
        Assert.True(doc.Undo.CanRedo);

        doc.Undo.Execute(cmd2); // should clear redo

        Assert.False(doc.Undo.CanRedo);
        Assert.Null(doc.Undo.NextRedoDescription);
    }

    [Fact]
    public void UndoMovesCommandFromUndoToRedoStack()
    {
        var doc = new TestDocument();
        var cmd = new TestCommand { Description = "edit1" };
        doc.Undo.Execute(cmd);

        doc.Undo.Undo();

        Assert.False(doc.Undo.CanUndo);
        Assert.True(doc.Undo.CanRedo);
        Assert.Equal("edit1", doc.Undo.NextRedoDescription);
        Assert.Contains("revert:edit1", doc.AppliedOperations);
    }

    [Fact]
    public void RedoReappliesCommandAndMovesBackToUndoStack()
    {
        var doc = new TestDocument();
        var cmd = new TestCommand { Description = "edit1" };
        doc.Undo.Execute(cmd);
        doc.Undo.Undo();

        doc.Undo.Redo();

        Assert.True(doc.Undo.CanUndo);
        Assert.False(doc.Undo.CanRedo);
        Assert.Equal("edit1", doc.Undo.NextUndoDescription);
        // apply was called twice: once from Execute, once from Redo
        Assert.Equal(2, doc.AppliedOperations.Count(op => op == "apply:edit1"));
    }

    [Fact]
    public void MergeableCommandsCollapseIntoOneUndoStep()
    {
        var doc = new TestDocument();
        // The UndoStack calls previous.TryMergeWith(incoming, out merged),
        // so the merge strategy must be on cmd1 (the command already on the stack).
        var mergedCmd = new TestCommand { Description = "merged" };
        var cmd1 = new TestCommand
        {
            Description = "type1",
            MergeStrategy = _ => mergedCmd  // accepts the incoming command and returns merged
        };
        var cmd2 = new TestCommand { Description = "type2" };

        doc.Undo.Execute(cmd1);
        doc.Undo.Execute(cmd2); // should merge — still only one entry on undo stack

        // Check description before draining the stack
        Assert.Equal("merged", doc.Undo.NextUndoDescription);
        Assert.Equal(1, CountUndoDepth(doc));
    }

    [Fact]
    public void ClearEmptiesBothStacks()
    {
        var doc = new TestDocument();
        doc.Undo.Execute(new TestCommand { Description = "edit1" });
        doc.Undo.Execute(new TestCommand { Description = "edit2" });
        doc.Undo.Undo();

        doc.Undo.Clear();

        Assert.False(doc.Undo.CanUndo);
        Assert.False(doc.Undo.CanRedo);
        Assert.Null(doc.Undo.NextUndoDescription);
        Assert.Null(doc.Undo.NextRedoDescription);
    }

    [Fact]
    public void NextUndoDescriptionReflectsTopCommandAfterExecute()
    {
        var doc = new TestDocument();
        doc.Undo.Execute(new TestCommand { Description = "first" });
        doc.Undo.Execute(new TestCommand { Description = "second" });

        Assert.Equal("second", doc.Undo.NextUndoDescription);
    }

    [Fact]
    public void NextRedoDescriptionReflectsTopCommandAfterUndo()
    {
        var doc = new TestDocument();
        doc.Undo.Execute(new TestCommand { Description = "first" });
        doc.Undo.Execute(new TestCommand { Description = "second" });
        doc.Undo.Undo();

        Assert.Equal("second", doc.Undo.NextRedoDescription);
    }

    [Fact]
    public void IsDirtyBecomesTrueAfterExecute()
    {
        var doc = new TestDocument();
        Assert.False(doc.IsDirty);

        doc.Undo.Execute(new TestCommand { Description = "edit1" });

        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void CanUndoAndCanRedoFlipCorrectly()
    {
        var doc = new TestDocument();
        Assert.False(doc.Undo.CanUndo);
        Assert.False(doc.Undo.CanRedo);

        doc.Undo.Execute(new TestCommand { Description = "edit1" });
        Assert.True(doc.Undo.CanUndo);
        Assert.False(doc.Undo.CanRedo);

        doc.Undo.Undo();
        Assert.False(doc.Undo.CanUndo);
        Assert.True(doc.Undo.CanRedo);

        doc.Undo.Redo();
        Assert.True(doc.Undo.CanUndo);
        Assert.False(doc.Undo.CanRedo);
    }

    // Helper: count how many undo steps exist by repeatedly undoing and counting.
    private static int CountUndoDepth(TestDocument doc)
    {
        int count = 0;
        while (doc.Undo.CanUndo) { doc.Undo.Undo(); count++; }
        return count;
    }
}
