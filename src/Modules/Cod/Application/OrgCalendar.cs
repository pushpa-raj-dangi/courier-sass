namespace Courier.Cod.Application;

// Day/month windows in the organization's local calendar, returned as half-open UTC ranges [from, to).
public static class OrgCalendar
{
    public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) Today(TimeZoneInfo tz, DateTimeOffset nowUtc)
    {
        var local = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var start = local.Date;
        return (ToUtc(start, tz), ToUtc(start.AddDays(1), tz));
    }

    public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) ThisMonth(TimeZoneInfo tz, DateTimeOffset nowUtc)
    {
        var local = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var start = new DateTime(local.Year, local.Month, 1);
        return (ToUtc(start, tz), ToUtc(start.AddMonths(1), tz));
    }

    private static DateTimeOffset ToUtc(DateTime localUnspecified, TimeZoneInfo tz)
    {
        var unspecified = DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, tz), TimeSpan.Zero);
    }
}
