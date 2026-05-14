using System;
using System.IO;
using System.Threading;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Editor.Text;
using Xunit;

namespace Phoenixml.Platform.Editor.Text.Tests;

public class TextDocumentTests
{
    [Fact]
    public void ConstructStoresText()
    {
        var doc = new TextDocument(DocumentId.New(), source: null, displayName: "x.xq", DocumentKind.XQuery, "hello");
        Assert.Equal("hello", doc.Text);
        Assert.Equal(1, doc.Version);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void ReplaceAllIncrementsVersionAndMarksDirty()
    {
        var doc = new TextDocument(DocumentId.New(), source: null, displayName: "x.xq", DocumentKind.XQuery, "old");
        doc.ReplaceAll("new");
        Assert.Equal("new", doc.Text);
        Assert.Equal(2, doc.Version);
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void ReplaceAllWithIdenticalTextIsNoOp()
    {
        var doc = new TextDocument(DocumentId.New(), source: null, displayName: "x.xq", DocumentKind.XQuery, "same");
        doc.ReplaceAll("same");
        Assert.Equal(1, doc.Version);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void KindFromExtensionMaps()
    {
        Assert.Equal(DocumentKind.XQuery, TextDocument.KindFromExtension("/x/y.xq"));
        Assert.Equal(DocumentKind.XQuery, TextDocument.KindFromExtension("/x/y.xqy"));
        Assert.Equal(DocumentKind.Xslt, TextDocument.KindFromExtension("/x/y.xsl"));
        Assert.Equal(DocumentKind.Xslt, TextDocument.KindFromExtension("/x/y.xslt"));
        Assert.Equal(DocumentKind.Unknown, TextDocument.KindFromExtension("/x/y.unknown"));
    }
}
