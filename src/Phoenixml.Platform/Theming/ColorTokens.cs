namespace Phoenixml.Platform.Theming;

/// <summary>Semantic color tokens. Themes resolve these to concrete hex colors.</summary>
public sealed record ColorTokens
{
    /// <summary>Gets the base surface color (background of windows, panels).</summary>
    public required string SurfaceBase { get; init; }

    /// <summary>Gets the raised surface color (cards, dropdowns).</summary>
    public required string SurfaceRaised { get; init; }

    /// <summary>Gets the sunken surface color (input fields, wells).</summary>
    public required string SurfaceSunken { get; init; }

    /// <summary>Gets the primary content color (body text, icons).</summary>
    public required string ContentPrimary { get; init; }

    /// <summary>Gets the secondary content color (captions, hints).</summary>
    public required string ContentSecondary { get; init; }

    /// <summary>Gets the disabled content color.</summary>
    public required string ContentDisabled { get; init; }

    /// <summary>Gets the Phoenixml brand accent color (teal).</summary>
    public required string AccentBrand { get; init; }

    /// <summary>Gets the per-product accent color (set by the host application).</summary>
    public required string AccentProduct { get; init; }

    /// <summary>Gets the border color.</summary>
    public required string Border { get; init; }

    /// <summary>Gets the focus ring color.</summary>
    public required string FocusRing { get; init; }

    /// <summary>Gets the success state color.</summary>
    public required string Success { get; init; }

    /// <summary>Gets the warning state color.</summary>
    public required string Warning { get; init; }

    /// <summary>Gets the error state color.</summary>
    public required string Error { get; init; }
}
