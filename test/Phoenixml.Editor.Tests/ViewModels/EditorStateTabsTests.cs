using System;
using Phoenixml.Editor.ViewModels;
using Phoenixml.Platform.Editor.Xml;
using Xunit;
using PlatformDocumentId = Phoenixml.Platform.Abstractions.Documents.DocumentId;

namespace Phoenixml.Editor.Tests.ViewModels;

public class EditorStateTabsTests
{
    private static XmlDocument MakeDoc(string displayName = "doc.xml")
    {
        var (result, nsTable) = XmlDocument.ParseXml("<root/>");
        return new XmlDocument(PlatformDocumentId.New(), source: null, displayName, result, nsTable);
    }

    [Fact]
    public void OpenDocumentAddsTab()
    {
        var state = new EditorState();
        var doc = MakeDoc("a.xml");

        var tab = state.OpenDocument(doc);

        var single = Assert.Single(state.Tabs);
        Assert.Same(tab, single);
        Assert.Same(tab, state.ActiveTab);
        Assert.Same(doc, tab.Document);
    }

    [Fact]
    public void OpenSameDocumentTwiceActivatesExistingTab()
    {
        var state = new EditorState();
        var doc = MakeDoc("a.xml");

        var tab1 = state.OpenDocument(doc);
        var tab2 = state.OpenDocument(doc);

        Assert.Single(state.Tabs);
        Assert.Same(tab1, tab2);
        Assert.Same(tab1, state.ActiveTab);
    }

    [Fact]
    public void OpenSecondDocumentAddsSecondTab()
    {
        var state = new EditorState();
        var docA = MakeDoc("a.xml");
        var docB = MakeDoc("b.xml");

        state.OpenDocument(docA);
        var tabB = state.OpenDocument(docB);

        Assert.Equal(2, state.Tabs.Count);
        Assert.Same(tabB, state.ActiveTab);
        Assert.Same(docB, state.ActiveTab!.Document);
    }

    [Fact]
    public void CloseActiveTabActivatesRightNeighbor()
    {
        var state = new EditorState();
        var tabA = state.OpenDocument(MakeDoc("a.xml"));
        var tabB = state.OpenDocument(MakeDoc("b.xml"));
        var tabC = state.OpenDocument(MakeDoc("c.xml"));

        // Activate B (middle), then close it — right neighbor is C.
        state.ActiveTab = tabB;
        state.CloseTab(tabB);

        Assert.Equal(2, state.Tabs.Count);
        Assert.Same(tabC, state.ActiveTab);
        _ = tabA; // referenced to suppress unused warning
    }

    [Fact]
    public void CloseLastTabClearsActive()
    {
        var state = new EditorState();
        var tab = state.OpenDocument(MakeDoc("a.xml"));

        state.CloseTab(tab);

        Assert.Empty(state.Tabs);
        Assert.Null(state.ActiveTab);
    }

    [Fact]
    public void CloseRightmostTabActivatesLeft()
    {
        var state = new EditorState();
        var tabA = state.OpenDocument(MakeDoc("a.xml"));
        var tabB = state.OpenDocument(MakeDoc("b.xml"));

        // B is already active (last opened); close it.
        state.CloseTab(tabB);

        Assert.Single(state.Tabs);
        Assert.Same(tabA, state.ActiveTab);
    }

    [Fact]
    public void ActiveDocumentSetterRoutesToOpenDocument()
    {
        var state = new EditorState();
        var doc = MakeDoc("a.xml");

        state.ActiveDocument = doc;

        Assert.Single(state.Tabs);
        Assert.Same(doc, state.ActiveDocument);
    }

    [Fact]
    public void ActiveDocumentChangedFiresOnTabActivation()
    {
        var state = new EditorState();
        var docA = MakeDoc("a.xml");
        var docB = MakeDoc("b.xml");
        var tabA = state.OpenDocument(docA);
        state.OpenDocument(docB);

        XmlDocument? fired = null;
        state.ActiveDocumentChanged += d => fired = d;

        state.ActiveTab = tabA;

        Assert.Same(docA, fired);
    }

    [Fact]
    public void ReplaceDocumentSwapsTabDocument()
    {
        var state = new EditorState();
        var docA = MakeDoc("a.xml");
        var docB = MakeDoc("b.xml");
        var tab = state.OpenDocument(docA);

        state.ReplaceDocument(docA, docB);

        Assert.Same(docB, tab.Document);
    }

    [Fact]
    public void ReplaceDocumentFiresActiveDocumentChangedWhenActive()
    {
        var state = new EditorState();
        var docA = MakeDoc("a.xml");
        var docB = MakeDoc("b.xml");
        var docFresh = MakeDoc("b-fresh.xml");
        state.OpenDocument(docA);
        state.OpenDocument(docB);
        // docB's tab is active (last opened)

        XmlDocument? fired = null;
        state.ActiveDocumentChanged += d => fired = d;

        state.ReplaceDocument(docB, docFresh);

        Assert.Same(docFresh, fired);
    }

    [Fact]
    public void ReplaceDocumentDoesNotFireWhenInactive()
    {
        var state = new EditorState();
        var docA = MakeDoc("a.xml");
        var docB = MakeDoc("b.xml");
        var docFresh = MakeDoc("b-fresh.xml");
        var tabA = state.OpenDocument(docA);
        state.OpenDocument(docB);
        // Activate A so B is inactive
        state.ActiveTab = tabA;

        XmlDocument? fired = null;
        state.ActiveDocumentChanged += d => fired = d;

        state.ReplaceDocument(docB, docFresh);

        Assert.Null(fired);
    }

    [Fact]
    public void ReplaceDocumentNoOpsWhenOldNotInTabs()
    {
        var state = new EditorState();
        var docA = MakeDoc("a.xml");
        var docUnrelated = MakeDoc("unrelated.xml");
        var docFresh = MakeDoc("fresh.xml");
        state.OpenDocument(docA);

        XmlDocument? fired = null;
        state.ActiveDocumentChanged += d => fired = d;

        // docUnrelated is not in any tab — should be a no-op
        state.ReplaceDocument(docUnrelated, docFresh);

        Assert.Same(docA, state.ActiveTab!.Document);
        Assert.Null(fired);
    }
}
