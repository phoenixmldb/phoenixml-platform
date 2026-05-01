namespace Phoenixml.Platform.Abstractions.Localization;

/// <summary>
/// Looks up localized strings by key. The default implementation reads from .resx
/// resources; replace it via DI to plug a different localization backend.
/// </summary>
/// <remarks>
/// v1 ships English-only. The interface exists so adding locales post-v1 is a
/// translation job, not an engineering refactor.
/// </remarks>
public interface IStringLocalizer
{
    /// <summary>Looks up the string for <paramref name="key"/>. Returns the key itself if not found.</summary>
    string this[string key] { get; }

    /// <summary>Looks up and formats; equivalent to <c>string.Format(this[key], args)</c>.</summary>
    string this[string key, params object[] args] { get; }
}
