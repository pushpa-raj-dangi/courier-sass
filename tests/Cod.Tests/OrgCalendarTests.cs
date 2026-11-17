using Courier.Cod.Application;

namespace Courier.Cod.Tests;

public class OrgCalendarTests
{
    private static readonly TimeZoneInfo Nepal =
        TimeZoneInfo.CreateCustomTimeZone("Asia/Kathmandu", TimeSpan.FromMinutes(345), "Nepal Time", "Nepal Time");

    private static DateTimeOffset Utc(string s) => DateTimeOffset.Parse(s, null, System.Globalization.DateTimeStyles.AssumeUniversal);

    [Fact]
    public void Today_AfterNepalMidnight_UsesNewNepalDay()
    {
        var (from, to) = OrgCalendar.Today(Nepal, Utc("2026-09-30T18:30:00Z"));
        Assert.Equal(Utc("2026-09-30T18:15:00Z"), from);
        Assert.Equal(Utc("2026-10-01T18:15:00Z"), to);
    }

    [Fact]
    public void ThisMonth_AfterNepalMidnight_StartsOnFirstOfNewMonth()
    {
        var (from, to) = OrgCalendar.ThisMonth(Nepal, Utc("2026-09-30T18:30:00Z"));
        Assert.Equal(Utc("2026-09-30T18:15:00Z"), from);
        Assert.Equal(Utc("2026-10-31T18:15:00Z"), to);
    }

    [Fact]
    public void Today_BeforeNepalMidnight_UsesPreviousNepalDay()
    {
        var (from, to) = OrgCalendar.Today(Nepal, Utc("2026-09-30T18:00:00Z"));
        Assert.Equal(Utc("2026-09-29T18:15:00Z"), from);
        Assert.Equal(Utc("2026-09-30T18:15:00Z"), to);
    }

    [Fact]
    public void ThisMonth_BeforeNepalMidnight_StartsOnFirstOfSameMonth()
    {
        var (from, to) = OrgCalendar.ThisMonth(Nepal, Utc("2026-09-30T18:00:00Z"));
        Assert.Equal(Utc("2026-08-31T18:15:00Z"), from);
        Assert.Equal(Utc("2026-09-30T18:15:00Z"), to);
    }
}
