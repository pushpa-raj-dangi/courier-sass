namespace Courier.Branches.Application;

public record BranchZoneInfo(Guid BranchId, Guid? DeliveryZoneId);

public interface IBranchLookupService
{
    // Null iff no branch with this id exists (or it's outside the caller's tenant, via the
    // existing query filter). A non-null result with DeliveryZoneId == null means the branch
    // exists but has no rating zone assigned yet.
    Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default);
}
