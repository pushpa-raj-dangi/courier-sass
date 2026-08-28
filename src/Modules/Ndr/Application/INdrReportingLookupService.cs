namespace Courier.Ndr.Application;

public interface INdrReportingLookupService
{
    Task<int> GetOpenNdrCountForBranchAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetShipmentIdsWithAnyNdrAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetShipmentIdsWithRtoAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default);
}
