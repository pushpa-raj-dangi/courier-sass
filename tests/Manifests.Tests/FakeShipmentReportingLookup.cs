using Courier.Shipments.Application;
using Courier.Shipments.Domain;

namespace Courier.Manifests.Tests;

// Shared, not file-local: every ManifestService construction in this project needs one. Manifests
// only calls the AWB lookup; the reporting reads throw so an accidental call fails loudly.
class FakeShipmentReportingLookup : IShipmentReportingLookupService
{
    public Dictionary<Guid, string> Awbs { get; } = new();
    public int AwbLookupCalls { get; private set; }

    public Task<IReadOnlyDictionary<Guid, string>> GetAwbsByShipmentIdsAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default)
    {
        AwbLookupCalls++;
        IReadOnlyDictionary<Guid, string> result = shipmentIds.Where(Awbs.ContainsKey).ToDictionary(id => id, id => Awbs[id]);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCohortStatusCountsAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchShipmentCounts> GetBranchShipmentCountsAsync(Guid branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TimeSpan?> GetAverageDeliveryTimeAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetShipmentIdsByDestinationBranchAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ShipmentExportRow>> GetShipmentExportRowsAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCurrentStatusCountsAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
