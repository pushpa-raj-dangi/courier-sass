using Courier.Cod.Application;
using Courier.SharedKernel;

namespace Courier.Cod.Tests;

// Shared across the CodService test classes (their other fakes are per-file `file class`es; these two
// are identical everywhere, so one internal copy avoids eleven duplicates).
internal class FixedTimeZoneLookup : IOrganizationTimeZoneLookup
{
    public static readonly TimeZoneInfo NepalTime =
        TimeZoneInfo.CreateCustomTimeZone("Asia/Kathmandu", TimeSpan.FromMinutes(345), "Nepal Time", "Nepal Time");

    public Task<TimeZoneInfo> GetTimeZoneAsync(Guid organizationId, CancellationToken ct = default) =>
        Task.FromResult(NepalTime);
}

internal class ZeroDeliveryLookup : IShipmentDeliveryLookupService
{
    public Task<int> CountDeliveredAsync(Guid merchantId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default) => Task.FromResult(0);
    public Task<bool> BelongsToMerchantAsync(Guid shipmentId, Guid merchantId, CancellationToken ct = default) => Task.FromResult(false);
}
