using Phoenixml.Platform.Theming;
using Xunit;

namespace Phoenixml.Platform.Tests.Theming;

/// <summary>
/// Tests for <see cref="ITheme.Apply"/>. Full resource-dictionary tests require
/// Avalonia.Headless (deferred to Task 9, Shell snapshot tests). These tests validate the
/// token content and guard against null-argument regressions.
/// </summary>
public class ThemeApplyTests
{
    [Fact]
    public void FluentDarkThemeApplyThrowsOnNullHost()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FluentDarkTheme.Default.Apply(null!));
    }

    [Fact]
    public void FluentLightThemeApplyThrowsOnNullHost()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FluentLightTheme.Default.Apply(null!));
    }

    [Fact]
    public void HighContrastThemeApplyThrowsOnNullHost()
    {
        Assert.Throws<ArgumentNullException>(() =>
            HighContrastTheme.Default.Apply(null!));
    }

    [Fact]
    public void AllColorTokenStringsAreValidHexColors()
    {
        // Verify all hex strings in each theme parse without throwing.
        // This catches typos in palette values before any Avalonia init is needed.
        foreach (var theme in new ITheme[] { FluentDarkTheme.Default, FluentLightTheme.Default, HighContrastTheme.Default })
        {
            var c = theme.Colors;
            AssertIsHexColor(c.SurfaceBase, nameof(c.SurfaceBase));
            AssertIsHexColor(c.SurfaceRaised, nameof(c.SurfaceRaised));
            AssertIsHexColor(c.SurfaceSunken, nameof(c.SurfaceSunken));
            AssertIsHexColor(c.ContentPrimary, nameof(c.ContentPrimary));
            AssertIsHexColor(c.ContentSecondary, nameof(c.ContentSecondary));
            AssertIsHexColor(c.ContentDisabled, nameof(c.ContentDisabled));
            AssertIsHexColor(c.AccentBrand, nameof(c.AccentBrand));
            AssertIsHexColor(c.AccentProduct, nameof(c.AccentProduct));
            AssertIsHexColor(c.Border, nameof(c.Border));
            AssertIsHexColor(c.FocusRing, nameof(c.FocusRing));
            AssertIsHexColor(c.Success, nameof(c.Success));
            AssertIsHexColor(c.Warning, nameof(c.Warning));
            AssertIsHexColor(c.Error, nameof(c.Error));
        }
    }

    private static void AssertIsHexColor(string value, string tokenName)
    {
        Assert.True(
            value.StartsWith('#') && (value.Length is 7 or 9) &&
            value[1..].All(c => Uri.IsHexDigit(c)),
            $"Token '{tokenName}' value '{value}' is not a valid #RRGGBB or #AARRGGBB hex color.");
    }
}
