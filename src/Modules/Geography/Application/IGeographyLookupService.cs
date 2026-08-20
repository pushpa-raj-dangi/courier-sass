namespace Courier.Geography.Application;

public interface IGeographyLookupService
{
    // True iff ward `wardNumber` actually exists under `localLevelId`, `localLevelId` actually
    // exists under `districtId`, and `districtId` actually exists under `provinceId` — i.e. the
    // full chain is a real, correctly nested Nepal geography path, not just four independently
    // valid ids.
    Task<bool> IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid localLevelId, int wardNumber, CancellationToken cancellationToken = default);

    // True iff a DeliveryZone with this id exists — scoped to the current tenant automatically via
    // AppDbContext's query filter on DeliveryZone (no explicit organizationId parameter needed).
    Task<bool> DeliveryZoneExistsAsync(Guid zoneId, CancellationToken cancellationToken = default);
}
