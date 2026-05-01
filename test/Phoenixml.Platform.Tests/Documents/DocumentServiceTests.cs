using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Documents;
using Phoenixml.Platform.Tests.Fixtures;
using Xunit;

namespace Phoenixml.Platform.Tests.Documents;

public class DocumentServiceTests
{
    [Fact]
    public void AddOpensDocumentAndMakesItActive()
    {
        var svc = new DocumentService();
        IDocument? opened = null;
        IDocument? activated = null;
        svc.DocumentOpened += d => opened = d;
        svc.ActiveDocumentChanged += d => activated = d;

        var doc = new TestDocument();
        svc.Add(doc);

        Assert.Contains(doc, svc.OpenDocuments);
        Assert.Equal(doc, svc.ActiveDocument);
        Assert.Equal(doc, opened);
        Assert.Equal(doc, activated);
    }

    [Fact]
    public async Task CloseAsyncReturnsFalseWhenCanCloseAsyncReturnsFalse()
    {
        var svc = new DocumentService();
        var doc = new TestDocument();
        doc.BlockClose();
        svc.Add(doc);

        var result = await svc.CloseAsync(doc, CancellationToken.None);

        Assert.False(result);
        Assert.Contains(doc, svc.OpenDocuments);
    }

    [Fact]
    public async Task CloseAsyncRemovesDocumentAndSwitchesActiveToMostRecent()
    {
        var svc = new DocumentService();
        var doc1 = new TestDocument();
        var doc2 = new TestDocument();
        svc.Add(doc1);
        svc.Add(doc2); // doc2 is now active

        var result = await svc.CloseAsync(doc2, CancellationToken.None);

        Assert.True(result);
        Assert.DoesNotContain(doc2, svc.OpenDocuments);
        Assert.Equal(doc1, svc.ActiveDocument);
    }

    [Fact]
    public void SetActiveRaisesActiveDocumentChangedOnlyOnRealTransitions()
    {
        var svc = new DocumentService();
        var doc = new TestDocument();
        svc.Add(doc);

        int changeCount = 0;
        svc.ActiveDocumentChanged += _ => changeCount++;

        svc.SetActive(doc); // no-op, already active
        svc.SetActive(doc); // no-op again

        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void SetActiveToNotOpenDocumentThrowsInvalidOperationException()
    {
        var svc = new DocumentService();
        var doc = new TestDocument(); // never added

        Assert.Throws<InvalidOperationException>(() => svc.SetActive(doc));
    }

    [Fact]
    public async Task ClosingDocumentNotInOpenListReturnsTrueIdempotent()
    {
        var svc = new DocumentService();
        var doc = new TestDocument(); // never added

        var result = await svc.CloseAsync(doc, CancellationToken.None);

        Assert.True(result);
    }
}
