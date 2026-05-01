namespace Phoenixml.Platform.Abstractions.Account;

/// <summary>Information about an available update.</summary>
public sealed record UpdateInfo(
    string LatestVersion,
    Uri ReleaseNotesUrl,
    Uri DownloadUrl,
    DateTimeOffset ReleasedAt,
    string Channel);

/// <summary>Surfaces update-availability state to the rest of the application.</summary>
public interface IUpdateChannel
{
    /// <summary>
    /// Most recently observed update info, or null if no check has succeeded yet.
    /// </summary>
    UpdateInfo? LatestKnown { get; }

    /// <summary>Raised when the latest-known update info changes.</summary>
#pragma warning disable CA1003 // Use generic event handler instances — intentional Action<T> pattern, consistent with IDocument.Changed
    event Action<UpdateInfo?>? Changed;
#pragma warning restore CA1003

    /// <summary>Triggers a check (typically piggybacked on the license-check ping). Honors per-product rate limits.</summary>
    ValueTask CheckAsync(CancellationToken ct);
}
