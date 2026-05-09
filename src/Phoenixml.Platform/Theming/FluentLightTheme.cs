using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Phoenixml.Platform.Theming;

/// <summary>Fluent light theme with stand-in palette (pending design contract per spec §4.4).</summary>
public sealed class FluentLightTheme : ITheme
{
    private static readonly ColorTokens DefaultColors = new()
    {
        SurfaceBase = "#FFFFFF",
        SurfaceRaised = "#F5F5F5",
        SurfaceSunken = "#EAEAEA",
        ContentPrimary = "#1F1F1F",
        ContentSecondary = "#5A5A5A",
        ContentDisabled = "#B0B0B0",
        AccentBrand = "#0E7C8C",
        AccentProduct = "#B85B3A",
        Border = "#D0D0D0",
        FocusRing = "#0E7C8C",
        Success = "#1A7F37",
        Warning = "#9A6700",
        Error = "#CF222E",
    };

    private static readonly SpacingTokens DefaultSpacing = new();

    private static readonly TypographyTokens DefaultTypography = new()
    {
        FontFamilyUi = "Inter, Segoe UI, system-ui, sans-serif",
        FontFamilyMono = "Cascadia Mono, JetBrains Mono, Menlo, Consolas, monospace",
    };

    /// <summary>Gets the singleton default instance of the light theme.</summary>
    public static readonly FluentLightTheme Default = new();

    /// <inheritdoc />
    public ThemeKind Kind => ThemeKind.Light;

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
