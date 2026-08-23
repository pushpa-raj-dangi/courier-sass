using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Manifests.Application;
using Courier.Manifests.Domain;
using Courier.Manifests.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Manifests.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FakeBranchLookup : IBranchLookupService
{
    private readonly HashSet<Guid> _knownBranchIds;
    public FakeBranchLookup(params Guid[] knownBranchIds) => _knownBranchIds = knownBranchIds.ToHashSet();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_knownBranchIds.Contains(branchId) ? new BranchZoneInfo(branchId, null) : null);
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class NotSupportedShipmentTransitionService : IShipmentTransitionService
{
    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

// Not file-scoped: returned from SeedReturnManifestWithOneShipmentAsync's tuple.
class RtoRecordingShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();
    public List<Guid> DispatchManyRtoCalls { get; } = new();
    public List<Guid> ReceiveRtoCalls { get; } = new();

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.TryGetValue(shipmentId, out var info) ? info : null);
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.Values.FirstOrDefault(s => s.Awb == awb));
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        foreach (var id in shipmentIds)
        {
            DispatchManyRtoCalls.Add(id);
            var info = ShipmentsById[id];
            ShipmentsById[id] = info with { CurrentStatus = ShipmentStatus.RtoInTransit, CurrentBranchId = null };
        }
        return Task.CompletedTask;
    }

    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        ReceiveRtoCalls.Add(shipmentId);
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.RtoAtOrigin, CurrentBranchId = info.OriginBranchId };
        return Task.FromResult<ShipmentResponse?>(null);
    }

    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class ManifestServicePurposeTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ManifestConfiguration());
            modelBuilder.ApplyConfiguration(new ManifestShipmentConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentTransferConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    [Fact]
    public async Task CreateManifestAsync_NoPurposeSpecified_DefaultsToForward()
    {
        await using var dbContext = CreateContext();
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var service = new ManifestService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(),
            new NotSupportedShipmentTransitionService(), new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId));

        var result = await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId), ActingUser(Guid.NewGuid()));

        Assert.Equal(ManifestPurpose.Forward, result.Purpose);
    }

    [Fact]
    public async Task ScanAsync_OnReturnPurposeManifest_ThrowsWithWrongPurposeReasonCode()
    {
        await using var dbContext = CreateContext();
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var service = new ManifestService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(),
            new NotSupportedShipmentTransitionService(), new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId));
        var manifest = await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId, ManifestPurpose.Return), ActingUser(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<ManifestScanRejectedException>(() =>
            service.ScanAsync(manifest.Id, new ScanShipmentRequest("AWB001"), ActingUser(Guid.NewGuid())));
        Assert.Equal("MANIFEST_WRONG_PURPOSE", ex.ReasonCode);
    }

    private static async Task<(ManifestService Service, RtoRecordingShipmentTransitionService Shipments, Guid ManifestId, Guid OriginalOriginBranchId, Guid OriginalDestinationBranchId)> SeedReturnManifestWithOneShipmentAsync(TestDbContext dbContext, Guid organizationId)
    {
        // Return-leg branch naming: the manifest travels FROM the original destination branch TO
        // the original origin branch (NdrService.CreateReturnShipmentAsync, Task 7, swaps them the
        // same way).
        var originalOriginBranchId = Guid.NewGuid();
        var originalDestinationBranchId = Guid.NewGuid();
        var shipments = new RtoRecordingShipmentTransitionService();
        var service = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(),
            shipments, new AllBranchesAuthorizationChecker(), new FakeBranchLookup(originalOriginBranchId, originalDestinationBranchId));

        var manifest = await service.CreateManifestAsync(new CreateManifestRequest(originalDestinationBranchId, originalOriginBranchId, ManifestPurpose.Return), ActingUser(Guid.NewGuid()));

        var shipmentId = Guid.NewGuid();
        shipments.ShipmentsById[shipmentId] = new ShipmentScanInfo(shipmentId, organizationId, "RTOAWB001", ShipmentStatus.RtoInitiated, originalDestinationBranchId, originalOriginBranchId, originalDestinationBranchId, null);

        return (service, shipments, manifest.Id, originalOriginBranchId, originalDestinationBranchId);
    }

    [Fact]
    public async Task FullReturnManifestLifecycle_DraftToClosed_MovesShipmentThroughRtoStatuses()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, originalOriginBranchId, _) = await SeedReturnManifestWithOneShipmentAsync(dbContext, Guid.NewGuid());
        var shipmentId = shipments.ShipmentsById.Keys.Single();

        var scanResult = await service.ScanRtoAsync(manifestId, new ScanShipmentRequest("RTOAWB001"), ActingUser(Guid.NewGuid()));
        Assert.Equal(shipmentId, scanResult.ShipmentId);
        Assert.Equal(ShipmentStatus.RtoInitiated, shipments.ShipmentsById[shipmentId].CurrentStatus); // no status change on scan itself

        await service.SealAsync(manifestId, ActingUser(Guid.NewGuid()));
        var dispatched = await service.DispatchAsync(manifestId, new DispatchManifestRequest("BA 2 KHA 5678", null), ActingUser(Guid.NewGuid()));
        Assert.Equal(ManifestStatus.InTransit, dispatched.Status);
        Assert.Single(shipments.DispatchManyRtoCalls, shipmentId);
        Assert.Equal(ShipmentStatus.RtoInTransit, shipments.ShipmentsById[shipmentId].CurrentStatus);

        var received = await service.ReceiveScanAsync(manifestId, new ScanShipmentRequest("RTOAWB001"), ActingUser(Guid.NewGuid()));
        Assert.NotNull(received.ReceivedAt);
        Assert.Single(shipments.ReceiveRtoCalls, shipmentId);
        Assert.Equal(ShipmentStatus.RtoAtOrigin, shipments.ShipmentsById[shipmentId].CurrentStatus);
        Assert.Equal(originalOriginBranchId, shipments.ShipmentsById[shipmentId].CurrentBranchId);

        var closed = await service.CloseAsync(manifestId, ActingUser(Guid.NewGuid()));
        Assert.Equal(ManifestStatus.Closed, closed.Status);
    }
}
