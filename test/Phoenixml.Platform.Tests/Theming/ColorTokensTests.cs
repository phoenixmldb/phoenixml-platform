using Phoenixml.Platform.Theming;
using Xunit;

namespace Phoenixml.Platform.Tests.Theming;

public class ColorTokensTests
{
    [Fact]
    public void FluentDarkThemeHasNonNullTokens()
    {
        var theme = FluentDarkTheme.Default;

        Assert.NotNull(theme.Colors.SurfaceBase);
        Assert.NotNull(theme.Colors.SurfaceRaised);
        Assert.NotNull(theme.Colors.SurfaceSunken);
        Assert.NotNull(theme.Colors.ContentPrimary);
        Assert.NotNull(theme.Colors.ContentSecondary);
        Assert.NotNull(theme.Colors.ContentDisabled);
        Assert.NotNull(theme.Colors.AccentBrand);
        Assert.NotNull(theme.Colors.AccentProduct);
        Assert.NotNull(theme.Colors.Border);
        Assert.NotNull(theme.Colors.FocusRing);
        Assert.NotNull(theme.Colors.Success);
        Assert.NotNull(theme.Colors.Warning);
        Assert.NotNull(theme.Colors.Error);
    }

    [Fact]
    public void FluentLightThemeHasNonNullTokens()
    {
        var theme = FluentLightTheme.Default;

        Assert.NotNull(theme.Colors.SurfaceBase);
        Assert.NotNull(theme.Colors.SurfaceRaised);
        Assert.NotNull(theme.Colors.SurfaceSunken);
        Assert.NotNull(theme.Colors.ContentPrimary);
        Assert.NotNull(theme.Colors.ContentSecondary);
        Assert.NotNull(theme.Colors.ContentDisabled);
        Assert.NotNull(theme.Colors.AccentBrand);
        Assert.NotNull(theme.Colors.AccentProduct);
        Assert.NotNull(theme.Colors.Border);
        Assert.NotNull(theme.Colors.FocusRing);
        Assert.NotNull(theme.Colors.Success);
        Assert.NotNull(theme.Colors.Warning);
        Assert.NotNull(theme.Colors.Error);
    }

    [Fact]
    public void HighContrastThemeHasNonNullTokens()
    {
        var theme = HighContrastTheme.Default;

        Assert.NotNull(theme.Colors.SurfaceBase);
        Assert.NotNull(theme.Colors.SurfaceRaised);
        Assert.NotNull(theme.Colors.SurfaceSunken);
        Assert.NotNull(theme.Colors.ContentPrimary);
        Assert.NotNull(theme.Colors.ContentSecondary);
        Assert.NotNull(theme.Colors.ContentDisabled);
        Assert.NotNull(theme.Colors.AccentBrand);
        Assert.NotNull(theme.Colors.AccentProduct);
        Assert.NotNull(theme.Colors.Border);
        Assert.NotNull(theme.Colors.FocusRing);
        Assert.NotNull(theme.Colors.Success);
        Assert.NotNull(theme.Colors.Warning);
        Assert.NotNull(theme.Colors.Error);
    }

    [Fact]
    public void LightAndDarkThemesHaveDifferentSurfaceBase()
    {
        Assert.NotEqual(
            FluentLightTheme.Default.Colors.SurfaceBase,
            FluentDarkTheme.Default.Colors.SurfaceBase);
    }

    [Fact]
    public void FluentDarkThemeHasExpectedKind()
    {
        Assert.Equal(ThemeKind.Dark, FluentDarkTheme.Default.Kind);
    }

    [Fact]
    public void FluentLightThemeHasExpectedKind()
    {
        Assert.Equal(ThemeKind.Light, FluentLightTheme.Default.Kind);
    }

    [Fact]
    public void HighContrastThemeHasExpectedKind()
    {
        Assert.Equal(ThemeKind.HighContrast, HighContrastTheme.Default.Kind);
    }

    [Fact]
    public void SpacingTokensHaveCorrectDefaults()
    {
        var spacing = new SpacingTokens();
        Assert.Equal(2, spacing.Xs);
        Assert.Equal(4, spacing.Sm);
        Assert.Equal(8, spacing.Md);
        Assert.Equal(16, spacing.Lg);
        Assert.Equal(24, spacing.Xl);
        Assert.Equal(32, spacing.Xxl);
    }

    [Fact]
    public void TypographyTokensHaveNonEmptyFontFamilies()
    {
        var dark = FluentDarkTheme.Default;
        Assert.NotEmpty(dark.Typography.FontFamilyUi);
        Assert.NotEmpty(dark.Typography.FontFamilyMono);
    }

    [Fact]
    public void AllThemesShareSameSpacingDefaults()
    {
        var dark = FluentDarkTheme.Default.Spacing;
        var light = FluentLightTheme.Default.Spacing;
        var hc = HighContrastTheme.Default.Spacing;

        Assert.Equal(dark.Xs, light.Xs);
        Assert.Equal(dark.Md, hc.Md);
    }
}
