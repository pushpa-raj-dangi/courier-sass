namespace Courier.Identity.Domain;

public static class OrganizationLocaleOptions
{
    public const string DefaultLocale = "en";
    public const string DefaultCalendar = "Gregorian";

    public static readonly IReadOnlyList<string> SupportedLocales = new[] { "en", "ne" };
    public static readonly IReadOnlyList<string> SupportedCalendars = new[] { "Gregorian", "BikramSambat" };
}
