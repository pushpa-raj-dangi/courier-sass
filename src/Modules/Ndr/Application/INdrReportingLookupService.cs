using Courier.Ndr.Domain;

namespace Courier.Ndr.Application;

public interface INdrReportingLookupService
{
    Task<int> GetOpenNdrCountForBranchAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetShipmentIdsWithAnyNdrAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetShipmentIdsWithRtoAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<NdrStatus, int>> GetStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);

    // F15 R3b-2 branches page: unresolved NDRs keyed by the shipment's DestinationBranchId - the
    // same join and rule as GetOpenNdrCountForBranchAsync, so the number matches the NDR queue.
    Task<IReadOnlyDictionary<Guid, int>> GetOpenNdrCountsByBranchAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);
}