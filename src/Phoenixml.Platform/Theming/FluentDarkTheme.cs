using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Phoenixml.Platform.Theming;

/// <summary>Fluent dark theme with VS Code-inspired stand-in palette (pending design contract per spec §4.4).</summary>
public sealed class FluentDarkTheme : ITheme
{
    private static readonly ColorTokens DefaultColors = new()
    {
        SurfaceBase = "#1E1E1E",
        SurfaceRaised = "#252525",
        SurfaceSunken = "#181818",
        ContentPrimary = "#E5E5E5",
        ContentSecondary = "#A0A0A0",
        ContentDisabled = "#555555",
        AccentBrand = "#2EA8C6",
        AccentProduct = "#D97757",
        Border = "#333333",
        FocusRing = "#2EA8C6",
        Success = "#3FB950",
        Warning = "#D29922",
        Error = "#F85149",
    };

    private static readonly SpacingTokens DefaultSpacing = new();

    private static readonly TypographyTokens DefaultTypography = new()
    {
        FontFamilyUi = "Inter, Segoe UI, system-ui, sans-serif",
        FontFamilyMono = "Cascadia Mono, JetBrains Mono, Menlo, Consolas, monospace",
    };

    /// <summary>Gets the singleton default instance of the dark theme.</summary>
    public static readonly FluentDarkTheme Default = new();

    /// <inheritdoc />
    public ThemeKind Kind => ThemeKind.Dark;

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
