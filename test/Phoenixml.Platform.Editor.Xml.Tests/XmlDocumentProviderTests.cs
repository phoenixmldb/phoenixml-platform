using System;
using System.IO;
using System.Threading.Tasks;
using Phoenixml.Platform.Editor.Xml;
using Xunit;

namespace Phoenixml.Platform.Editor.Xml.Tests;

public class XmlDocumentProviderTests
{
    private static XmlDocumentProvider CreateProvider() => new();

    [Theory]
    [InlineData(".xml")]
    [InlineData(".xsd")]
    [InlineData(".xsl")]
    [InlineData(".xslt")]
    public void CanOpenKnownExtensionsReturnsTrue(string ext)
    {
        var provider = CreateProvider();
        var uri = new Uri($"file:///tmp/file{ext}");
        Assert.True(provider.CanOpen(uri));
    }

    [Theory]
    [InlineData(".json")]
    [InlineData(".txt")]
    [InlineData(".pdf")]
    public void CanOpenUnknownExtensionsReturnsFalse(string ext)
    {
        var provider = CreateProvider();
        var uri = new Uri($"file:///tmp/file{ext}");
        Assert.False(provider.CanOpen(uri));
    }

    [Fact]
    public void CanOpenNonFileUriReturnsFalse()
    {
        var provider = CreateProvider();
        var uri = new Uri("http://example.com/foo.xml");
        Assert.False(provider.CanOpen(uri));
    }

    [Fact]
    public async Task NewAsyncReturnsEmptyDocument()
    {
        var provider = CreateProvider();
        var doc = await provider.NewAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.NotNull(doc);
        Assert.Equal("untitled.xml", doc.DisplayName);
        var xmlDoc = Assert.IsType<XmlDocument>(doc);
        Assert.NotNull(xmlDoc.Root);
        // The root XDM document should have a document element named "root"
        Assert.NotNull(xmlDoc.Root.DocumentElement);
        var rootElem = xmlDoc.Resolve(xmlDoc.Root.DocumentElement!.Value);
        Assert.NotNull(rootElem);
        var elem = Assert.IsType<PhoenixmlDb.Xdm.Nodes.XdmElement>(rootElem);
        Assert.Equal("root", elem.LocalName);
    }

    [Fact]
    public async Task OpenAsyncNonFileUriThrows()
    {
        var provider = CreateProvider();
        var uri = new Uri("http://example.com/foo.xml");
        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.OpenAsync(uri, TestContext.Current.CancellationToken).AsTask()).ConfigureAwait(true);
    }

    [Fact]
    public async Task OpenAsyncLoadsDocument()
    {
        var dir = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "data.xml");
        try
        {
            await File.WriteAllTextAsync(path, "<data/>", TestContext.Current.CancellationToken).ConfigureAwait(true);
            var provider = CreateProvider();
            var doc = await provider.OpenAsync(new Uri(path), TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.NotNull(doc);
            Assert.Equal("data.xml", doc.DisplayName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
