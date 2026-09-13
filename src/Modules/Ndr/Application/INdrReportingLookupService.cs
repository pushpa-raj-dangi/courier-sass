using Courier.Ndr.Domain;

namespace Courier.Ndr.Application;

public interface INdrReportingLookupService
{
    Task<int> GetOpenNdrCountForBranchAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetShipmentIdsWithAnyNdrAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetShipmentIdsWithRtoAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<NdrStatus, int>> GetStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);
}
