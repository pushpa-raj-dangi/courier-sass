using System.Security.Claims;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Manifests.Application;
using Courier.Ndr.Application;
using Courier.Riders.Application;
using Courier.SharedKernel;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;

namespace Courier.Reports.Tests;

internal sealed class FakeBranchLookupService : IBranchLookupService
{
    public Task<List<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public BranchZoneInfo? ZoneInfo { get; set; }
    public IReadOnlyDictionary<Guid, string> Names { get; set; } = new Dictionary<Guid, string>();

    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(ZoneInfo);
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) => Task.FromResult(Names);
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

internal sealed class FakeBranchAuthorizationChecker : IBranchAuthorizationChecker
{
    public IReadOnlySet<Guid>? AccessibleBranchIds { get; set; }

    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(AccessibleBranchIds);

    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(AccessibleBranchIds is null || AccessibleBranchIds.Contains(branchId));
}

internal sealed class FakeShipmentReportingLookupService : IShipmentReportingLookupService
{
    public IReadOnlyDictionary<ShipmentStatus, int> CohortStatusCounts { get; set; } = new Dictionary<ShipmentStatus, int>();
    public BranchShipmentCounts BranchShipmentCounts { get; set; } = new(0, 0, 0);
    public TimeSpan? AverageDeliveryTime { get; set; }
    public IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> ShipmentIdsByDestinationBranch { get; set; } = new Dictionary<Guid, IReadOnlyList<Guid>>();
    public IReadOnlyList<ShipmentExportRow> ExportRows { get; set; } = new List<ShipmentExportRow>();
    public IReadOnlyDictionary<Guid, string> Awbs { get; set; } = new Dictionary<Guid, string>();

    public Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCohortStatusCountsAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(CohortStatusCounts);
    public Task<BranchShipmentCounts> GetBranchShipmentCountsAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(BranchShipmentCounts);
    public Task<TimeSpan?> GetAverageDeliveryTimeAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(AverageDeliveryTime);
    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetShipmentIdsByDestinationBranchAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(ShipmentIdsByDestinationBranch);
    public Task<IReadOnlyList<ShipmentExportRow>> GetShipmentExportRowsAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(ExportRows);
    public Task<IReadOnlyDictionary<Guid, string>> GetAwbsByShipmentIdsAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default) => Task.FromResult(Awbs);
}

internal sealed class FakeManifestReportingLookupService : IManifestReportingLookupService
{
    public BranchManifestCounts ManifestCounts { get; set; } = new(0, 0);
    public int MissingShipmentExceptionCount { get; set; }

    public Task<BranchManifestCounts> GetBranchManifestCountsAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(ManifestCounts);
    public Task<int> GetOpenMissingShipmentExceptionCountAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(MissingShipmentExceptionCount);
}

internal sealed class FakeRiderReportingLookupService : IRiderReportingLookupService
{
    public IReadOnlyList<RiderTaskLoad> RiderTaskLoad { get; set; } = new List<RiderTaskLoad>();
    public IReadOnlyDictionary<Guid, DeliverySuccessStats> SuccessRateByBranch { get; set; } = new Dictionary<Guid, DeliverySuccessStats>();
    public IReadOnlyDictionary<Guid, DeliverySuccessStats> SuccessRateByRider { get; set; } = new Dictionary<Guid, DeliverySuccessStats>();

    public Task<IReadOnlyList<RiderTaskLoad>> GetBranchRiderTaskLoadAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(RiderTaskLoad);
    public Task<IReadOnlyDictionary<Guid, DeliverySuccessStats>> GetSuccessRateByBranchAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(SuccessRateByBranch);
    public Task<IReadOnlyDictionary<Guid, DeliverySuccessStats>> GetSuccessRateByRiderAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(SuccessRateByRider);
}

internal sealed class FakeNdrReportingLookupService : INdrReportingLookupService
{
    public int OpenNdrCountForBranch { get; set; }
    public IReadOnlySet<Guid> ShipmentIdsWithAnyNdr { get; set; } = new HashSet<Guid>();
    public IReadOnlySet<Guid> ShipmentIdsWithRto { get; set; } = new HashSet<Guid>();

    public Task<int> GetOpenNdrCountForBranchAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(OpenNdrCountForBranch);
    public Task<IReadOnlySet<Guid>> GetShipmentIdsWithAnyNdrAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default) => Task.FromResult(ShipmentIdsWithAnyNdr);
    public Task<IReadOnlySet<Guid>> GetShipmentIdsWithRtoAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default) => Task.FromResult(ShipmentIdsWithRto);
}

internal sealed class FakeCodReportingLookupService : ICodReportingLookupService
{
    public CodFinancialSummary FinancialSummary { get; set; } = new(0, 0, 0, 0, 0);
    public decimal BranchPendingFinanceVerificationTotal { get; set; }
    public IReadOnlyList<CodExportRow> ExportRows { get; set; } = new List<CodExportRow>();

    public Task<CodFinancialSummary> GetOrgFinancialSummaryAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default) => Task.FromResult(FinancialSummary);
    public Task<decimal> GetBranchPendingFinanceVerificationTotalAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(BranchPendingFinanceVerificationTotal);
    public Task<IReadOnlyList<CodExportRow>> GetCodExportRowsAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(ExportRows);
}
