using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Phoenixml.Platform.Theming;

/// <summary>High-contrast accessibility theme with pure black/white extremes.</summary>
public sealed class HighContrastTheme : ITheme
{
    private static readonly ColorTokens DefaultColors = new()
    {
        SurfaceBase = "#000000",
        SurfaceRaised = "#000000",
        SurfaceSunken = "#000000",
        ContentPrimary = "#FFFFFF",
        ContentSecondary = "#FFFFFF",
        ContentDisabled = "#888888",
        AccentBrand = "#00FFFF",
        AccentProduct = "#FFFF00",
        Border = "#FFFFFF",
        FocusRing = "#FFFF00",
        Success = "#00FF00",
        Warning = "#FFFF00",
        Error = "#FF0000",
    };

    private static readonly SpacingTokens DefaultSpacing = new();

    private static readonly TypographyTokens DefaultTypography = new()
    {
        FontFamilyUi = "Inter, Segoe UI, system-ui, sans-serif",
        FontFamilyMono = "Cascadia Mono, JetBrains Mono, Menlo, Consolas, monospace",
    };

    /// <summary>Gets the singleton default instance of the high-contrast theme.</summary>
    public static readonly HighContrastTheme Default = new();

    /// <inheritdoc />
    public ThemeKind Kind => ThemeKind.HighContrast;

    /// <inheritdoc />
    public ColorTokens Colors => DefaultColors;

    /// <inheritdoc />
    public SpacingTokens Spacing => DefaultSpacing;

    /// <inheritdoc />
    public TypographyTokens Typography => DefaultTypography;

    /// <inheritdoc />
    public void Apply(IResourceHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host is not StyledElement styledElement)
            throw new ArgumentException("host must be a StyledElement (e.g. Application, Window).", nameof(host));
        var res = styledElement.Resources;
        res["Phoenixml.Color.Surface.Base"] = Color.Parse(Colors.SurfaceBase);
        res["Phoenixml.Color.Surface.Raised"] = Color.Parse(Colors.SurfaceRaised);
        res["Phoenixml.Color.Surface.Sunken"] = Color.Parse(Colors.SurfaceSunken);
        res["Phoenixml.Color.Content.Primary"] = Color.Parse(Colors.ContentPrimary);
        res["Phoenixml.Color.Content.Secondary"] = Color.Parse(Colors.ContentSecondary);
        res["Phoenixml.Color.Content.Disabled"] = Color.Parse(Colors.ContentDisabled);
        res["Phoenixml.Color.Accent.Brand"] = Color.Parse(Colors.AccentBrand);
        res["Phoenixml.Color.Accent.Product"] = Color.Parse(Colors.AccentProduct);
        res["Phoenixml.Color.Border"] = Color.Parse(Colors.Border);
        res["Phoenixml.Color.FocusRing"] = Color.Parse(Colors.FocusRing);
        res["Phoenixml.Color.Success"] = Color.Parse(Colors.Success);
        res["Phoenixml.Color.Warning"] = Color.Parse(Colors.Warning);
        res["Phoenixml.Color.Error"] = Color.Parse(Colors.Error);
        res["Phoenixml.Spacing.Xs"] = Spacing.Xs;
        res["Phoenixml.Spacing.Sm"] = Spacing.Sm;
        res["Phoenixml.Spacing.Md"] = Spacing.Md;
        res["Phoenixml.Spacing.Lg"] = Spacing.Lg;
        res["Phoenixml.Spacing.Xl"] = Spacing.Xl;
        res["Phoenixml.Spacing.Xxl"] = Spacing.Xxl;
        res["Phoenixml.Typography.FontFamily.Ui"] = Typography.FontFamilyUi;
        res["Phoenixml.Typography.FontFamily.Mono"] = Typography.FontFamilyMono;
        res["Phoenixml.Typography.FontSize.Body"] = Typography.FontSizeBody;
        res["Phoenixml.Typography.FontSize.Caption"] = Typography.FontSizeCaption;
        res["Phoenixml.Typography.FontSize.Heading"] = Typography.FontSizeHeading;
    }
}
