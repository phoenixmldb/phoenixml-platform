using Phoenixml.Platform.Shell;
using Xunit;

namespace Phoenixml.Platform.Tests.Shell;

public class ChromePlatformTests
{
    [Fact]
    public void DetectReturnsNonNullInstance()
    {
        // Detect() should always return a valid configuration regardless of the host OS.
        var chrome = ChromePlatform.Detect();
        Assert.NotNull(chrome);
    }

    [Fact]
    public void DetectExtendsClientAreaByDefault()
    {
        // All platforms extend the client area — the custom title bar is always drawn.
        var chrome = ChromePlatform.Detect();
        Assert.True(chrome.ExtendsClientArea);
    }

    [Fact]
    public void DetectOnWindowsShowsWindowsChromeButtons()
    {
        if (!OperatingSystem.IsWindows()) return; // skip on non-Windows
        var chrome = ChromePlatform.Detect();
        Assert.True(chrome.ShowWindowsChromeButtons);
        Assert.False(chrome.UseTrafficLightsNative);
    }

    [Fact]
    public void DetectOnMacOSUsesTrafficLightsNative()
    {
        if (!OperatingSystem.IsMacOS()) return; // skip on non-macOS
        var chrome = ChromePlatform.Detect();
        Assert.True(chrome.UseTrafficLightsNative);
        Assert.False(chrome.ShowWindowsChromeButtons);
    }

    [Fact]
    public void DetectOnLinuxShowsWindowsChromeButtons()
    {
        if (!OperatingSystem.IsLinux()) return; // skip on non-Linux
        var chrome = ChromePlatform.Detect();
        Assert.True(chrome.ShowWindowsChromeButtons);
        Assert.False(chrome.UseTrafficLightsNative);
    }

    [Fact]
    public void CustomBuildsConfigurationWithSpecifiedFlags()
    {
        var chrome = ChromePlatform.Custom(
            showWindowsChromeButtons: true,
            useTrafficLightsNative: false,
            extendsClientArea: false,
            fallbackReason: "test override");

        Assert.True(chrome.ShowWindowsChromeButtons);
        Assert.False(chrome.UseTrafficLightsNative);
        Assert.False(chrome.ExtendsClientArea);
        Assert.Equal("test override", chrome.FallbackReason);
    }

    [Fact]
    public void CustomDefaultExtendsClientAreaIsTrue()
    {
        var chrome = ChromePlatform.Custom();
        Assert.True(chrome.ExtendsClientArea);
    }

    [Fact]
    public void CustomFallbackReasonNullByDefault()
    {
        var chrome = ChromePlatform.Custom();
        Assert.Null(chrome.FallbackReason);
    }
}
