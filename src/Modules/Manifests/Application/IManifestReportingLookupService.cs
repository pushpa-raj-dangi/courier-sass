using Courier.Manifests.Domain;

namespace Courier.Manifests.Application;

public record BranchManifestCounts(int InTransitTowardBranch, int RecentlyClosed);

public interface IManifestReportingLookupService
{
    Task<BranchManifestCounts> GetBranchManifestCountsAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<int> GetOpenMissingShipmentExceptionCountAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<ManifestStatus, int>> GetStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);
}
