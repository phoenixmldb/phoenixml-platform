namespace Phoenixml.Platform.Abstractions.Account;

/// <summary>Surfaces account and license state to the rest of the application.</summary>
public interface IAccountStatus
{
    /// <summary>Currently-stored license, or null if the user has not registered.</summary>
    License? CurrentLicense { get; }

    /// <summary>True iff <see cref="CurrentLicense"/> is non-null and within its grace window.</summary>
    bool IsActive { get; }

    /// <summary>Raised when the license is issued, renewed, revoked, or cleared.</summary>
#pragma warning disable CA1003 // Use generic event handler instances — intentional Action<T> pattern, consistent with IDocument.Changed
    event Action<License?>? Changed;
#pragma warning restore CA1003
}
