using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Phoenixml.Workbench.Clipboard;
using Xunit;

namespace Phoenixml.Editor.Tests.Clipboard;

/// <summary>
/// Unit tests for <see cref="ClipboardService"/>. Uses a stub <see cref="IClipboardBackend"/>
/// (in-memory dictionary) so no running Avalonia application is required.
/// </summary>
public sealed class ClipboardServiceTests
{
    private sealed class StubClipboardBackend : IClipboardBackend
    {
        private readonly Dictionary<string, string> _store = new(StringComparer.Ordinal);
        private string? _plainText;

        public Task SetFormatsAsync(IReadOnlyDictionary<string, string> payload)
        {
            foreach (var (format, value) in payload)
            {
                _store[format] = value;
                if (string.Equals(format, ClipboardFormats.Text, StringComparison.Ordinal))
                    _plainText = value;
            }
            return Task.CompletedTask;
        }

        public Task<string?> GetDataAsync(string format)
        {
            _store.TryGetValue(format, out var value);
            return Task.FromResult<string?>(value);
        }

        public Task<string?> GetTextAsync() => Task.FromResult(_plainText);

        public Task SetTextAsync(string text)
        {
            _plainText = text;
            return Task.CompletedTask;
        }

        public bool HasFormat(string format) => _store.ContainsKey(format);

        public string? GetRaw(string format) =>
            _store.TryGetValue(format, out var v) ? v : null;

        public void SetPlainText(string text)
        {
            _plainText = text;
            _store[ClipboardFormats.Text] = text;
        }
    }

    [Fact]
    public async Task WriteXmlSetsBothFormats()
    {
        var stub = new StubClipboardBackend();
        var sut = new ClipboardService(stub);
        const string xml = "<root><child/></root>";

        await sut.WriteXmlAsync(xml);

        Assert.True(stub.HasFormat(ClipboardFormats.Xml), "application/xml format missing");
        Assert.Equal(xml, stub.GetRaw(ClipboardFormats.Xml));
        Assert.True(stub.HasFormat(ClipboardFormats.Text), "text/plain format missing");
        Assert.Equal(xml, stub.GetRaw(ClipboardFormats.Text));
    }

    [Fact]
    public async Task ReadXmlReturnsApplicationXmlWhenPresent()
    {
        var stub = new StubClipboardBackend();
        var sut = new ClipboardService(stub);
        const string xml = "<book isbn=\"123\"><title>Test</title></book>";

        await sut.WriteXmlAsync(xml);
        var result = await sut.ReadXmlAsync();

        Assert.Equal(xml, result);
    }

    [Fact]
    public async Task ReadXmlFallsBackToPlainTextWhenNoApplicationXml()
    {
        var stub = new StubClipboardBackend();
        var sut = new ClipboardService(stub);
        const string plainXml = "<note>hello</note>";
        stub.SetPlainText(plainXml);

        var result = await sut.ReadXmlAsync();

        Assert.Equal(plainXml, result);
    }

    [Fact]
    public async Task ReadXmlReturnsNullWhenEmpty()
    {
        var stub = new StubClipboardBackend();
        var sut = new ClipboardService(stub);

        var result = await sut.ReadXmlAsync();

        Assert.Null(result);
    }
}
