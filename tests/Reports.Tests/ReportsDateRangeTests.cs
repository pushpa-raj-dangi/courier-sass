using Courier.Reports.Application;

namespace Courier.Reports.Tests;

public class ReportsDateRangeTests
{
    [Fact]
    public void Resolve_BothNull_DefaultsToTrailing30Days()
    {
        var (from, to) = ReportsDateRange.Resolve(null, null);
        Assert.True((to - from) > TimeSpan.FromDays(29) && (to - from) < TimeSpan.FromDays(31));
    }

    [Fact]
    public void Resolve_FromAfterTo_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<InvalidDateRangeException>(() => ReportsDateRange.Resolve(now, now.AddDays(-1)));
    }

    [Fact]
    public void Resolve_RangeOverOneYear_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<InvalidDateRangeException>(() => ReportsDateRange.Resolve(now.AddDays(-400), now));
    }

    [Fact]
    public void Resolve_ValidExplicitRange_ReturnsItUnchanged()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-10);
        var to = DateTimeOffset.UtcNow;
        var (resolvedFrom, resolvedTo) = ReportsDateRange.Resolve(from, to);
        Assert.Equal(from, resolvedFrom);
        Assert.Equal(to, resolvedTo);
    }
}
