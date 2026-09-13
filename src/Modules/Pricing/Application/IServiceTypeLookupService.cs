namespace Courier.Pricing.Application;

public interface IServiceTypeLookupService
{
    // ServiceType is global reference data (no OrganizationId — see ServiceType.cs's own doc
    // comment), so this check is deliberately not tenant-scoped.
    Task<bool> ExistsAsync(Guid serviceTypeId, CancellationToken cancellationToken = default);
}
