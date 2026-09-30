using System.Text.RegularExpressions;

namespace Courier.Identity.Domain;

// An organization's AWB prefix: the leading part of every AWB it issues
// ({AwbPrefix}{yyyyMMdd}{6-digit sequence}). Stored on Organization and unique across all
// organizations — AWB uniqueness is only indexed per (OrganizationId, Awb), so a globally unique
// prefix is what keeps two tenants from ever issuing the same AWB (public /track/{awb} has no
// tenant and fails closed on an ambiguous AWB). Equal-length AWBs have equal-length prefixes, so
// distinct prefixes cannot produce the same AWB string.
public static partial class AwbPrefix
{
    public const int MaxLength = 6;

    // The org's Slug uppercased, non-alphanumerics stripped, truncated to MaxLength. A slug made
    // entirely of symbols falls back to the first hex chars of the organization id.
    public static string Derive(string? slug, Guid organizationId)
    {
        var raw = NonAlphanumericRegex().Replace(slug?.ToUpperInvariant() ?? string.Empty, string.Empty);
        return raw.Length > 0 ? raw[..Math.Min(MaxLength, raw.Length)] : organizationId.ToString("N")[..MaxLength].ToUpperInvariant();
    }

    // Attempt 1 is the base itself; attempt n >= 2 replaces the base's tail with n so the result
    // stays within MaxLength (INTLOR -> INTLO2 ... INTL10; AB -> AB2).
    public static string Candidate(string basePrefix, int attempt)
    {
        if (attempt <= 1)
        {
            return basePrefix;
        }
        var suffix = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (suffix.Length >= MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), "No AWB prefix candidates left for this base.");
        }
        return basePrefix[..Math.Min(basePrefix.Length, MaxLength - suffix.Length)] + suffix;
    }

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex NonAlphanumericRegex();
}
