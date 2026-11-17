namespace Courier.SharedKernel;

// Resolves an organization's configured time zone (OrganizationSettings.TimeZoneId). Declared in
// SharedKernel so modules that cannot reference Identity (e.g. Cod) can still bucket "today" and
// "this month" by the organization's local calendar.
public interface IOrganizationTimeZoneLookup
{
    Task<TimeZoneInfo> GetTimeZoneAsync(Guid organizationId, CancellationToken ct = default);
}
