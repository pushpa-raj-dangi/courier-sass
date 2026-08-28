using System.Globalization;
using System.Security.Claims;
using Courier.Merchants.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Reports.Infrastructure;
using Courier.SharedKernel;
using Courier.Shipments.Application;

namespace Courier.Reports.Application;

public class ReportExportService
{
    private readonly IShipmentReportingLookupService _shipmentLookup;
    private readonly ICodReportingLookupService _codLookup;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IMerchantLookupService _merchantLookupService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public ReportExportService(
        IShipmentReportingLookupService shipmentLookup,
        ICodReportingLookupService codLookup,
        IBranchLookupService branchLookupService,
        IMerchantLookupService merchantLookupService,
        IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _shipmentLookup = shipmentLookup;
        _codLookup = codLookup;
        _branchLookupService = branchLookupService;
        _merchantLookupService = merchantLookupService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    public async Task<string> GetShipmentsCsvAsync(Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var (rangeFrom, rangeTo) = ReportsDateRange.Resolve(from, to);
        var branchIds = await ResolveBranchScopeAsync(branchId, actingUser, cancellationToken);

        var rows = await _shipmentLookup.GetShipmentExportRowsAsync(branchIds, rangeFrom, rangeTo, cancellationToken);

        var branchNameIds = rows.SelectMany(r => new[] { r.OriginBranchId, r.DestinationBranchId }).ToHashSet();
        var merchantIds = rows.Select(r => r.MerchantId).ToHashSet();
        var branchNames = await _branchLookupService.GetNamesByIdsAsync(branchNameIds, cancellationToken);
        var merchantNames = await _merchantLookupService.GetDisplayNamesByIdsAsync(merchantIds, cancellationToken);

        var headers = new[] { "Awb", "Status", "OriginBranch", "DestinationBranch", "MerchantName", "CodAmount", "TotalCharge", "CreatedAt", "DeliveredAt" };
        var csvRows = rows.Select(r => (IReadOnlyList<string?>)new[]
        {
            r.Awb,
            r.Status.ToString(),
            branchNames.GetValueOrDefault(r.OriginBranchId, string.Empty),
            branchNames.GetValueOrDefault(r.DestinationBranchId, string.Empty),
            merchantNames.GetValueOrDefault(r.MerchantId, string.Empty),
            r.CodAmount.ToString(CultureInfo.InvariantCulture),
            r.TotalCharge.ToString(CultureInfo.InvariantCulture),
            r.CreatedAt.ToString("O"),
            r.DeliveredAt?.ToString("O")
        });

        return CsvWriter.Write(headers, csvRows);
    }

    public async Task<string> GetCodSummaryCsvAsync(Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var (rangeFrom, rangeTo) = ReportsDateRange.Resolve(from, to);
        var branchIds = await ResolveBranchScopeAsync(branchId, actingUser, cancellationToken);

        var rows = await _codLookup.GetCodExportRowsAsync(branchIds, rangeFrom, rangeTo, cancellationToken);

        var shipmentIds = rows.Select(r => r.ShipmentId).ToHashSet();
        var branchIdsForNames = rows.Select(r => r.BranchId).ToHashSet();
        var merchantIds = rows.Select(r => r.MerchantId).ToHashSet();
        var awbs = await _shipmentLookup.GetAwbsByShipmentIdsAsync(shipmentIds, cancellationToken);
        var branchNames = await _branchLookupService.GetNamesByIdsAsync(branchIdsForNames, cancellationToken);
        var merchantNames = await _merchantLookupService.GetDisplayNamesByIdsAsync(merchantIds, cancellationToken);

        var headers = new[] { "Awb", "MerchantName", "BranchName", "ExpectedAmount", "CollectedAmount", "Status", "CollectedAt" };
        var csvRows = rows.Select(r => (IReadOnlyList<string?>)new[]
        {
            awbs.GetValueOrDefault(r.ShipmentId, string.Empty),
            merchantNames.GetValueOrDefault(r.MerchantId, string.Empty),
            branchNames.GetValueOrDefault(r.BranchId, string.Empty),
            r.ExpectedAmount.ToString(CultureInfo.InvariantCulture),
            r.CollectedAmount?.ToString(CultureInfo.InvariantCulture),
            r.Status.ToString(),
            r.CollectedAt?.ToString("O")
        });

        return CsvWriter.Write(headers, csvRows);
    }

    private async Task<IReadOnlySet<Guid>?> ResolveBranchScopeAsync(Guid? requestedBranchId, ClaimsPrincipal actingUser, CancellationToken cancellationToken)
    {
        var accessible = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "report.export", cancellationToken);
        if (requestedBranchId is not Guid branchId)
        {
            return accessible;
        }
        if (accessible is not null && !accessible.Contains(branchId))
        {
            throw new ReportsValidationException("Branch is outside your branch access for permission 'report.export'.");
        }
        return new HashSet<Guid> { branchId };
    }
}
