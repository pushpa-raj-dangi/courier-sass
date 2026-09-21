namespace Courier.Notifications.Domain;

// Mirrors Identity's OrganizationLocaleOptions.SupportedLocales without referencing Identity
// (Notifications must not take a project reference to it — see IOrganizationLocaleLookup).
public static class NotificationLocales
{
    public const string Default = "en";

    public static readonly IReadOnlyList<string> Supported = new[] { "en", "ne" };

    public static bool IsSupported(string? locale) => locale is not null && Supported.Contains(locale);

    public static string Normalize(string? locale)
    {
        var lowered = locale?.ToLowerInvariant();
        return IsSupported(lowered) ? lowered! : Default;
    }
}
