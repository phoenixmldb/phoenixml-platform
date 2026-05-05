using System;
using System.IO;
using System.Threading.Tasks;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using Phoenixml.Platform.Abstractions.Documents;
using Phoenixml.Platform.Editor.Xml;
using Xunit;
using PlatformDocumentId = Phoenixml.Platform.Abstractions.Documents.DocumentId;

namespace Phoenixml.Platform.Editor.Xml.Tests;

public class XmlDocumentTests
{
    private const string SimpleXml = "<root><child attr=\"hello\">text</child></root>";

    [Fact]
    public async Task LoadAsyncParsesValidXmlReturnsDocument()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.xml");
        try
        {
            await File.WriteAllTextAsync(path, SimpleXml, TestContext.Current.CancellationToken).ConfigureAwait(true);
            var doc = await XmlDocument.LoadAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.NotNull(doc.Root);
            Assert.Equal("test.xml", doc.DisplayName);
            Assert.Equal(DocumentKind.Xml, doc.Kind);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveReturnsNodeForKnownIdNullForUnknown()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.xml");
        try
        {
            await File.WriteAllTextAsync(path, SimpleXml, TestContext.Current.CancellationToken).ConfigureAwait(true);
            var doc = await XmlDocument.LoadAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);

            // The document node itself should resolve
            var docNode = doc.Resolve(doc.Root.Id);
            Assert.NotNull(docNode);
            Assert.IsType<XdmDocument>(docNode);

            // A synthetic ID should not resolve
            var unknown = doc.Resolve(new NodeId(ulong.MaxValue));
            Assert.Null(unknown);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsAsyncRoundTripsXml()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var srcPath = Path.Combine(dir, "source.xml");
        var dstPath = Path.Combine(dir, "output.xml");
        try
        {
            await File.WriteAllTextAsync(srcPath, SimpleXml, TestContext.Current.CancellationToken).ConfigureAwait(true);
            var doc = await XmlDocument.LoadAsync(srcPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await doc.SaveAsAsync(new Uri(dstPath), TestContext.Current.CancellationToken).ConfigureAwait(true);

            // Reload and verify structure
            var reloaded = await XmlDocument.LoadAsync(dstPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.NotNull(reloaded.Root);
            Assert.NotNull(reloaded.Root.DocumentElement);

            // The root element should be named "root"
            var rootElem = reloaded.Resolve(reloaded.Root.DocumentElement!.Value);
            Assert.NotNull(rootElem);
            var elem = Assert.IsType<XdmElement>(rootElem);
            Assert.Equal("root", elem.LocalName);
            Assert.Single(elem.Children);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsyncWithoutSourceThrows()
    {
        var (result, nsTable) = XmlDocument.ParseXml("<root/>");
        var doc = new XmlDocument(
            PlatformDocumentId.New(),
            source: null,
            "untitled.xml",
            result,
            nsTable);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => doc.SaveAsync(TestContext.Current.CancellationToken).AsTask()).ConfigureAwait(true);
    }
}
