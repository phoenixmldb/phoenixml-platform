namespace Phoenixml.Platform.Theming;

/// <summary>Spacing scale tokens (4 px base grid).</summary>
public sealed record SpacingTokens
{
    /// <summary>Gets the extra-small spacing value (2 px).</summary>
    public double Xs { get; init; } = 2;

    /// <summary>Gets the small spacing value (4 px).</summary>
    public double Sm { get; init; } = 4;

    /// <summary>Gets the medium spacing value (8 px).</summary>
    public double Md { get; init; } = 8;

    /// <summary>Gets the large spacing value (16 px).</summary>
    public double Lg { get; init; } = 16;

    /// <summary>Gets the extra-large spacing value (24 px).</summary>
    public double Xl { get; init; } = 24;

    /// <summary>Gets the double-extra-large spacing value (32 px).</summary>
    public double Xxl { get; init; } = 32;
}
