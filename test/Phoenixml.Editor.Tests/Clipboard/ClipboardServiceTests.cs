using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Input;
using Phoenixml.Editor.Clipboard;
using Xunit;

namespace Phoenixml.Editor.Tests.Clipboard;

/// <summary>
/// Unit tests for <see cref="ClipboardService"/>. Uses a stub <see cref="IClipboardBackend"/>
/// (in-memory dictionary) so no running Avalonia application is required.
/// </summary>
public sealed class ClipboardServiceTests
{
    // -------------------------------------------------------------------------
    // Stub
    // -------------------------------------------------------------------------

    /// <summary>
    /// Minimal in-memory <see cref="IClipboardBackend"/> that stores data objects in a plain
    /// dictionary keyed by format string. Mirrors the three operations that
    /// <see cref="ClipboardService"/> calls.
    /// </summary>
    private sealed class StubClipboardBackend : IClipboardBackend
    {
        private readonly Dictionary<string, object> _store = new(StringComparer.Ordinal);
        private string? _plainText;

        /// <inheritdoc/>
        public Task SetDataObjectAsync(IDataObject data)
        {
            foreach (var format in data.GetDataFormats())
            {
                var value = data.Get(format);
                if (value is not null)
                    _store[format] = value;
                if (string.Equals(format, DataFormats.Text, StringComparison.Ordinal)
                    && value is string text)
                    _plainText = text;
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<object?> GetDataAsync(string format)
        {
            _store.TryGetValue(format, out var value);
            return Task.FromResult(value);
        }

        /// <inheritdoc/>
        public Task<string?> GetTextAsync() => Task.FromResult(_plainText);

        /// <inheritdoc/>
        public Task SetTextAsync(string text)
        {
            _plainText = text;
            return Task.CompletedTask;
        }

        // -------------------------------------------------------------------------
        // Test-only inspection helpers
        // -------------------------------------------------------------------------

        /// <summary>Returns whether the given format was stored.</summary>
        public bool HasFormat(string format) => _store.ContainsKey(format);

        /// <summary>Returns the raw stored value for a format, or <see langword="null"/>.</summary>
        public object? GetRaw(string format) =>
            _store.TryGetValue(format, out var v) ? v : null;

        /// <summary>Seeds plain text without going through a DataObject.</summary>
        public void SetPlainText(string text)
        {
            _plainText = text;
            _store[DataFormats.Text] = text;
        }
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    /// <summary>
    /// After a WriteXmlAsync call both the application/xml format and the text/plain
    /// format must be present on the clipboard with the correct value.
    /// </summary>
    [Fact]
    public async Task WriteXmlSetsBothFormats()
    {
        var stub = new StubClipboardBackend();
        var sut = new ClipboardService(stub);
        const string xml = "<root><child/></root>";

        await sut.WriteXmlAsync(xml);

        Assert.True(stub.HasFormat("application/xml"), "application/xml format missing");
        Assert.Equal(xml, stub.GetRaw("application/xml") as string);

        Assert.True(stub.HasFormat(DataFormats.Text), "text/plain format missing");
        Assert.Equal(xml, stub.GetRaw(DataFormats.Text) as string);
    }

    /// <summary>
    /// When the clipboard contains an application/xml entry, ReadXmlAsync must return it
    /// (rather than falling back to plain text).
    /// </summary>
    [Fact]
    public async Task ReadXmlReturnsApplicationXmlWhenPresent()
    {
        var stub = new StubClipboardBackend();
        var sut = new ClipboardService(stub);
        const string xml = "<book isbn=\"123\"><title>Test</title></book>";

        // Populate both formats via WriteXmlAsync.
        await sut.WriteXmlAsync(xml);

        var result = await sut.ReadXmlAsync();

        Assert.Equal(xml, result);
    }

    /// <summary>
    /// When the clipboard has no application/xml entry but does have plain text, ReadXmlAsync
    /// must return the plain-text value (cross-application paste path).
    /// </summary>
    [Fact]
    public async Task ReadXmlFallsBackToPlainTextWhenNoApplicationXml()
    {
        var stub = new StubClipboardBackend();
        var sut = new ClipboardService(stub);

        // Seed only plain text — no application/xml entry.
        const string plainXml = "<note>hello</note>";
        stub.SetPlainText(plainXml);

        var result = await sut.ReadXmlAsync();

        Assert.Equal(plainXml, result);
    }

    /// <summary>
    /// When the clipboard is completely empty (no application/xml, no plain text),
    /// ReadXmlAsync must return <see langword="null"/> rather than an empty string.
    /// </summary>
    [Fact]
    public async Task ReadXmlReturnsNullWhenEmpty()
    {
        var stub = new StubClipboardBackend();
        var sut = new ClipboardService(stub);

        var result = await sut.ReadXmlAsync();

        Assert.Null(result);
    }
}
