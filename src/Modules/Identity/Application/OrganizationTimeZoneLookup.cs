using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class OrganizationTimeZoneLookup : IOrganizationTimeZoneLookup
{
    private const string DefaultTimeZoneId = "Asia/Kathmandu";

    private readonly DbContext _dbContext;

    public OrganizationTimeZoneLookup(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TimeZoneInfo> GetTimeZoneAsync(Guid organizationId, CancellationToken ct = default)
    {
        var timeZoneId = await _dbContext.Set<OrganizationSettings>().AsNoTracking()
            .Where(s => s.OrganizationId == organizationId)
            .Select(s => s.TimeZoneId)
            .FirstOrDefaultAsync(ct);

        return TryResolve(timeZoneId) ?? TryResolve(DefaultTimeZoneId) ?? NepalFixedOffset();
    }

    private static TimeZoneInfo? TryResolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    // Nepal has no DST, so a fixed +05:45 zone is an exact stand-in when the platform has no tz data.
    private static TimeZoneInfo NepalFixedOffset() =>
        TimeZoneInfo.CreateCustomTimeZone(DefaultTimeZoneId, TimeSpan.FromMinutes(345), "Nepal Time", "Nepal Time");
}
