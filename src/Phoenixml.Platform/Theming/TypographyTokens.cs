namespace Phoenixml.Platform.Theming;

/// <summary>Typography tokens: font families and size scale.</summary>
public sealed record TypographyTokens
{
    /// <summary>Gets the UI font family stack.</summary>
    public required string FontFamilyUi { get; init; }

    /// <summary>Gets the monospace font family stack.</summary>
    public required string FontFamilyMono { get; init; }

    /// <summary>Gets the body text font size in device-independent pixels.</summary>
    public double FontSizeBody { get; init; } = 13;

    /// <summary>Gets the caption font size in device-independent pixels.</summary>
    public double FontSizeCaption { get; init; } = 11;

    /// <summary>Gets the heading font size in device-independent pixels.</summary>
    public double FontSizeHeading { get; init; } = 16;
}
