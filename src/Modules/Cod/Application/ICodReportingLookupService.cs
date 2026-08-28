using Courier.Cod.Domain;

namespace Courier.Cod.Application;

public record CodFinancialSummary(decimal Expected, decimal Collected, decimal Pending, decimal Settled, decimal MerchantPayable);

public record CodExportRow(Guid ShipmentId, Guid MerchantId, Guid BranchId, decimal ExpectedAmount, decimal? CollectedAmount, CodCollectionStatus Status, DateTimeOffset? CollectedAt);

public interface ICodReportingLookupService
{
    Task<CodFinancialSummary> GetOrgFinancialSummaryAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);

    Task<decimal> GetBranchPendingFinanceVerificationTotalAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CodExportRow>> GetCodExportRowsAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
