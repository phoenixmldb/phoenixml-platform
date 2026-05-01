using System.Globalization;
using Phoenixml.Platform.Documents.Internal;
using Xunit;

namespace Phoenixml.Platform.Tests.Localization;

public class EnglishOnlyStringLocalizerTests
{
    private readonly EnglishOnlyStringLocalizer _localizer = new();

    [Fact]
    public void ReturnsKeyWhenNoArgs()
    {
        var result = _localizer["Hello"];
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void FormatsWithInvariantCultureWhenArgsSupplied()
    {
        var result = _localizer["Hello {0}", "world"];
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void DecimalFormattingUsesInvariantCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            // Use a culture where decimal separator is comma
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var result = _localizer["Value: {0}", 1.5m];
            Assert.Equal("Value: 1.5", result);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
