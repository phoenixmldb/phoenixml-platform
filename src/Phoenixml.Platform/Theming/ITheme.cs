using Avalonia;
using Avalonia.Controls;

namespace Phoenixml.Platform.Theming;

/// <summary>A resolved theme that can apply itself to an Avalonia resource host.</summary>
public interface ITheme
{
    /// <summary>Gets the theme variant.</summary>
    ThemeKind Kind { get; }

    /// <summary>Gets the color tokens for this theme.</summary>
    ColorTokens Colors { get; }

    /// <summary>Gets the spacing tokens for this theme.</summary>
    SpacingTokens Spacing { get; }

    /// <summary>Gets the typography tokens for this theme.</summary>
    TypographyTokens Typography { get; }

    /// <summary>
    /// Applies the theme to the given resource host so XAML can bind via
    /// <c>{DynamicResource Phoenixml.Color.Surface.Base}</c> etc.
    /// The <paramref name="host"/> must be a <see cref="StyledElement"/>
    /// (e.g. <see cref="Application"/>, <c>Window</c>) to provide a writable resource dictionary.
    /// </summary>
    /// <param name="host">The Avalonia resource host to write tokens into.</param>
    void Apply(IResourceHost host);
}
