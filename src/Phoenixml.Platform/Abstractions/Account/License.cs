namespace Phoenixml.Platform.Abstractions.Account;

/// <summary>Decoded license claims; mirrors the JWT body documented in the licensing API spec.</summary>
public sealed record License(
    string LicenseId,
    string Product,
    string EmailHash,
    bool MarketingConsent,
    string Tier,
    DateTimeOffset IssuedAt,
    DateTimeOffset ValidThrough);
