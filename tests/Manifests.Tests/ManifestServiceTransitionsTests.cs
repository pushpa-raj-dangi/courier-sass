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
    public Task<List<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    private readonly HashSet<Guid> _knownBranchIds;
    public FakeBranchLookup(params Guid[] knownBranchIds) => _knownBranchIds = knownBranchIds.ToHashSet();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_knownBranchIds.Contains(branchId) ? new BranchZoneInfo(branchId, null) : null);
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

// Not file-local (unlike this test project's other fakes): SeedSealedManifestWithOneShipmentAsync
// below returns it as part of a tuple, and a file-local type cannot appear in a member signature
// of this non-file-local test class (see ManifestServiceScanningTests.cs's identically-shaped
// ScanningFakeShipmentTransitionService for the same reasoning) — named uniquely per file to avoid
// a duplicate-type collision with that file's fake in the same namespace.
class TransitionsFakeShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();
    public List<Guid> DispatchManyCalls { get; } = new();
    public List<Guid> ReceiveCalls { get; } = new();

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.TryGetValue(shipmentId, out var info) ? info : null);

    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.Values.FirstOrDefault(s => s.Awb == awb));

    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.Sorted };
        return Task.FromResult<ShipmentResponse?>(null);
    }

    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        foreach (var id in shipmentIds)
        {
            DispatchManyCalls.Add(id);
            var info = ShipmentsById[id];
            ShipmentsById[id] = info with { CurrentStatus = ShipmentStatus.InTransit, CurrentBranchId = null };
        }
        return Task.CompletedTask;
    }

    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        ReceiveCalls.Add(shipmentId);
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.AtDestinationBranch, CurrentBranchId = info.DestinationBranchId };
        return Task.FromResult<ShipmentResponse?>(null);
    }

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

public class ManifestServiceTransitionsTests
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

    private static async Task<(ManifestService Service, TransitionsFakeShipmentTransitionService Shipments, Guid ManifestId, Guid FromBranchId, Guid ToBranchId, Guid ShipmentId, string Awb)> SeedSealedManifestWithOneShipmentAsync(TestDbContext dbContext, Guid organizationId)
    {
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var shipments = new TransitionsFakeShipmentTransitionService();
        var service = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments,
            new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId));

        var manifest = await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId), ActingUser(Guid.NewGuid()));
        var shipmentId = Guid.NewGuid();
        var awb = "AWB001";
        shipments.ShipmentsById[shipmentId] = new ShipmentScanInfo(shipmentId, organizationId, awb, ShipmentStatus.AtOriginBranch, fromBranchId, fromBranchId, toBranchId, null);
        await service.ScanAsync(manifest.Id, new ScanShipmentRequest(awb), ActingUser(Guid.NewGuid()));
        await service.SealAsync(manifest.Id, ActingUser(Guid.NewGuid()));

        return (service, shipments, manifest.Id, fromBranchId, toBranchId, shipmentId, awb);
    }

    [Fact]
    public async Task DispatchAsync_SealedManifest_MovesToInTransitAndDispatchesAllShipments()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, _, _, shipmentId, _) = await SeedSealedManifestWithOneShipmentAsync(dbContext, Guid.NewGuid());

        var result = await service.DispatchAsync(manifestId, new DispatchManifestRequest("BA 2 KHA 1234", null), ActingUser(Guid.NewGuid()));

        Assert.Equal(ManifestStatus.InTransit, result.Status);
        Assert.Equal("BA 2 KHA 1234", result.VehiclePlate);
        Assert.Contains(shipmentId, shipments.DispatchManyCalls);
    }

    [Fact]
    public async Task DispatchAsync_DraftManifest_Throws()
    {
        await using var dbContext = CreateContext();
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var shipments = new TransitionsFakeShipmentTransitionService();
        var service = new ManifestService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), shipments,
            new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId));
        var manifest = await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId), ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<ManifestValidationException>(() =>
            service.DispatchAsync(manifest.Id, new DispatchManifestRequest("BA 2 KHA 1234", null), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task DispatchAsync_MissingVehiclePlate_Throws()
    {
        await using var dbContext = CreateContext();
        var (service, _, manifestId, _, _, _, _) = await SeedSealedManifestWithOneShipmentAsync(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<ManifestValidationException>(() =>
            service.DispatchAsync(manifestId, new DispatchManifestRequest("", null), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task ReceiveScanAsync_ShipmentInTransitOnThisManifest_MarksReceivedAndWritesTransfer()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipments, manifestId, fromBranchId, toBranchId, shipmentId, awb) = await SeedSealedManifestWithOneShipmentAsync(dbContext, organizationId);
        await service.DispatchAsync(manifestId, new DispatchManifestRequest("BA 2 KHA 1234", null), ActingUser(Guid.NewGuid()));

        var result = await service.ReceiveScanAsync(manifestId, new ScanShipmentRequest(awb), ActingUser(Guid.NewGuid()));

        Assert.Equal(shipmentId, result.ShipmentId);
        Assert.NotNull(result.ReceivedAt);
        Assert.Contains(shipmentId, shipments.ReceiveCalls);

        var manifest = await dbContext.Set<Manifest>().FirstAsync(m => m.Id == manifestId);
        Assert.Equal(ManifestStatus.Received, manifest.Status);

        var transfer = await dbContext.Set<ShipmentTransfer>().SingleAsync(t => t.ManifestId == manifestId);
        Assert.Equal(shipmentId, transfer.ShipmentId);
        Assert.Equal(fromBranchId, transfer.FromBranchId);
        Assert.Equal(toBranchId, transfer.ToBranchId);
    }

    [Fact]
    public async Task ReceiveScanAsync_ShipmentNotOnThisManifest_ThrowsWithNotOnManifestReasonCode()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipments, manifestId, _, _, _, _) = await SeedSealedManifestWithOneShipmentAsync(dbContext, organizationId);
        await service.DispatchAsync(manifestId, new DispatchManifestRequest("BA 2 KHA 1234", null), ActingUser(Guid.NewGuid()));

        var strangerId = Guid.NewGuid();
        shipments.ShipmentsById[strangerId] = new ShipmentScanInfo(strangerId, organizationId, "AWB999", ShipmentStatus.InTransit, null, Guid.NewGuid(), Guid.NewGuid(), null);

        var ex = await Assert.ThrowsAsync<ManifestScanRejectedException>(() =>
            service.ReceiveScanAsync(manifestId, new ScanShipmentRequest("AWB999"), ActingUser(Guid.NewGuid())));
        Assert.Equal("NOT_ON_MANIFEST", ex.ReasonCode);
    }

    [Fact]
    public async Task ReceiveScanAsync_AlreadyReceived_ThrowsWithAlreadyReceivedReasonCode()
    {
        await using var dbContext = CreateContext();
        var (service, _, manifestId, _, _, _, awb) = await SeedSealedManifestWithOneShipmentAsync(dbContext, Guid.NewGuid());
        await service.DispatchAsync(manifestId, new DispatchManifestRequest("BA 2 KHA 1234", null), ActingUser(Guid.NewGuid()));
        await service.ReceiveScanAsync(manifestId, new ScanShipmentRequest(awb), ActingUser(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<ManifestScanRejectedException>(() =>
            service.ReceiveScanAsync(manifestId, new ScanShipmentRequest(awb), ActingUser(Guid.NewGuid())));
        Assert.Equal("ALREADY_RECEIVED", ex.ReasonCode);
    }

    [Fact]
    public async Task CloseAsync_ReceivedManifestWithUnscannedShipment_ClosesAndSurfacesMissingShipment()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var shipments = new TransitionsFakeShipmentTransitionService();
        var service = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments,
            new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId));
        var manifest = await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId), ActingUser(Guid.NewGuid()));

        var receivedShipmentId = Guid.NewGuid();
        shipments.ShipmentsById[receivedShipmentId] = new ShipmentScanInfo(receivedShipmentId, organizationId, "AWB-RECEIVED", ShipmentStatus.AtOriginBranch, fromBranchId, fromBranchId, toBranchId, null);
        await service.ScanAsync(manifest.Id, new ScanShipmentRequest("AWB-RECEIVED"), ActingUser(Guid.NewGuid()));

        var missingShipmentId = Guid.NewGuid();
        shipments.ShipmentsById[missingShipmentId] = new ShipmentScanInfo(missingShipmentId, organizationId, "AWB-MISSING", ShipmentStatus.AtOriginBranch, fromBranchId, fromBranchId, toBranchId, null);
        await service.ScanAsync(manifest.Id, new ScanShipmentRequest("AWB-MISSING"), ActingUser(Guid.NewGuid()));

        await service.SealAsync(manifest.Id, ActingUser(Guid.NewGuid()));
        await service.DispatchAsync(manifest.Id, new DispatchManifestRequest("BA 2 KHA 1234", null), ActingUser(Guid.NewGuid()));
        await service.ReceiveScanAsync(manifest.Id, new ScanShipmentRequest("AWB-RECEIVED"), ActingUser(Guid.NewGuid()));

        var closed = await service.CloseAsync(manifest.Id, ActingUser(Guid.NewGuid()));
        Assert.Equal(ManifestStatus.Closed, closed.Status);

        var missing = await service.GetMissingShipmentsAsync(manifest.Id);
        var missingShipment = Assert.Single(missing!);
        Assert.Equal(missingShipmentId, missingShipment.ShipmentId);
        Assert.Equal("AWB-MISSING", missingShipment.Awb);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownManifest_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = new ManifestService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new TransitionsFakeShipmentTransitionService(),
            new AllBranchesAuthorizationChecker(), new FakeBranchLookup());

        Assert.Null(await service.GetByIdAsync(Guid.NewGuid()));
    }
}
