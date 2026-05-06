using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

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
        // Both Application and StyledElement expose Resources via separate hierarchies in
        // Avalonia 11; accept either. IResourceHost itself doesn't surface Resources in 11.x.
        IResourceDictionary res = host switch
        {
            Application app => app.Resources,
            StyledElement styled => styled.Resources,
            _ => throw new ArgumentException("host must be Application or StyledElement.", nameof(host)),
        };
        res["Phoenixml.Color.Surface.Base"] = new ImmutableSolidColorBrush(Color.Parse(Colors.SurfaceBase));
        res["Phoenixml.Color.Surface.Raised"] = new ImmutableSolidColorBrush(Color.Parse(Colors.SurfaceRaised));
        res["Phoenixml.Color.Surface.Sunken"] = new ImmutableSolidColorBrush(Color.Parse(Colors.SurfaceSunken));
        res["Phoenixml.Color.Content.Primary"] = new ImmutableSolidColorBrush(Color.Parse(Colors.ContentPrimary));
        res["Phoenixml.Color.Content.Secondary"] = new ImmutableSolidColorBrush(Color.Parse(Colors.ContentSecondary));
        res["Phoenixml.Color.Content.Disabled"] = new ImmutableSolidColorBrush(Color.Parse(Colors.ContentDisabled));
        res["Phoenixml.Color.Accent.Brand"] = new ImmutableSolidColorBrush(Color.Parse(Colors.AccentBrand));
        res["Phoenixml.Color.Accent.Product"] = new ImmutableSolidColorBrush(Color.Parse(Colors.AccentProduct));
        res["Phoenixml.Color.Border"] = new ImmutableSolidColorBrush(Color.Parse(Colors.Border));
        res["Phoenixml.Color.FocusRing"] = new ImmutableSolidColorBrush(Color.Parse(Colors.FocusRing));
        res["Phoenixml.Color.Success"] = new ImmutableSolidColorBrush(Color.Parse(Colors.Success));
        res["Phoenixml.Color.Warning"] = new ImmutableSolidColorBrush(Color.Parse(Colors.Warning));
        res["Phoenixml.Color.Error"] = new ImmutableSolidColorBrush(Color.Parse(Colors.Error));
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
