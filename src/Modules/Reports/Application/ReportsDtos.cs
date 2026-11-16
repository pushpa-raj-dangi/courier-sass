using Courier.Cod.Application;
using Courier.Manifests.Application;
using Courier.Riders.Application;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;

namespace Courier.Reports.Application;

public sealed class ReportsValidationException : Exception
{
    public ReportsValidationException(string message) : base(message)
    {
    }
}

public sealed class InvalidDateRangeException : Exception
{
    public InvalidDateRangeException(string message) : base(message)
    {
    }
}

public static class ReportsDateRange
{
    public static (DateTimeOffset From, DateTimeOffset To) Resolve(DateTimeOffset? from, DateTimeOffset? to)
    {
        var resolvedTo = to ?? DateTimeOffset.UtcNow;
        var resolvedFrom = from ?? resolvedTo.AddDays(-30);
        if (resolvedFrom > resolvedTo)
        {
            throw new InvalidDateRangeException("'from' must not be after 'to'.");
        }
        if (resolvedTo - resolvedFrom > TimeSpan.FromDays(366))
        {
            throw new InvalidDateRangeException("Date range must not exceed 1 year.");
        }
        return (resolvedFrom, resolvedTo);
    }
}

public record BranchPerformance(
    int ShipmentVolume, int DeliveryAttempts, int DeliverySuccesses, double? DeliverySuccessRate,
    int NdrCount, double? NdrRate, int RtoCount, double? RtoRate);

public record RiderPerformance(Guid RiderId, int Attempts, int Successes, double? SuccessRate);

public record OrgDashboardResponse(
    IReadOnlyDictionary<ShipmentStatus, int> TodaysShipmentsByStatus,
    CodFinancialSummary Financial,
    IReadOnlyDictionary<Guid, BranchPerformance> BranchPerformance,
    IReadOnlyList<RiderPerformance> RiderPerformance,
    double? OverallDeliverySuccessRate,
    double? OverallNdrRate,
    double? OverallRtoRate,
    TimeSpan? AverageDeliveryTime);

public record BranchDashboardResponse(
    BranchShipmentCounts ShipmentCounts,
    BranchManifestCounts ManifestCounts,
    int MissingShipmentExceptions,
    IReadOnlyList<RiderTaskLoad> Riders,
    DeliverySuccessStats TodaysDeliveries,
    decimal CodPendingFinanceVerification,
    int OpenNdrCount);

// F15 R3b-2 branches page live counts. A scoped caller gets one row per branch in scope (zero
// rows included); an all-branches caller gets one row per branch with any activity, and a branch
// missing from Items has all zeros. OpenExceptions = unresolved NDRs by destination branch.
public record BranchActivityRow(Guid BranchId, int OnHand, int OutForDelivery, int RidersOut, int OpenExceptions);

public record BranchActivityResponse(IReadOnlyList<BranchActivityRow> Items);
