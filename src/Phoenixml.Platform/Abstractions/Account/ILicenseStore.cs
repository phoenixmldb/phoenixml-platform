namespace Phoenixml.Platform.Abstractions.Account;

/// <summary>
/// Persists the license JWT. Implementations are platform-specific: DPAPI on Windows,
/// Keychain on macOS, libsecret on Linux. Fallback to an obfuscated file is permitted
/// but discouraged.
/// </summary>
public interface ILicenseStore
{
    /// <summary>Loads the stored JWT, or returns <see langword="null"/> if none is stored.</summary>
    ValueTask<string?> LoadAsync(CancellationToken ct);

    /// <summary>Saves <paramref name="jwt"/> to the platform-specific secret store.</summary>
    ValueTask SaveAsync(string jwt, CancellationToken ct);

    /// <summary>Removes any stored JWT from the secret store.</summary>
    ValueTask ClearAsync(CancellationToken ct);
}
