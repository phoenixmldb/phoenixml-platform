using System.Globalization;
using Phoenixml.Platform.Abstractions.Localization;

namespace Phoenixml.Platform.Documents.Internal;

/// <summary>
/// Default <see cref="IStringLocalizer"/> implementation. Returns the key itself,
/// formatted via <see cref="CultureInfo.InvariantCulture"/> when args are supplied.
/// English-only by design for v1; replace via DI to plug a real localization backend.
/// </summary>
internal sealed class EnglishOnlyStringLocalizer : IStringLocalizer
{
    /// <inheritdoc/>
    public string this[string key] => key;

    /// <inheritdoc/>
    public string this[string key, params object[] args] =>
        args is { Length: > 0 }
            ? string.Format(CultureInfo.InvariantCulture, key, args)
            : key;
}
