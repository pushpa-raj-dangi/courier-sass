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
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

// In-memory stand-in for the real Shipments-module ShipmentService — lets ManifestService's scan
// logic be unit-tested without needing Shipments' full EF model, mirroring how Shipments.Tests
// itself fakes IBranchLookupService/IGeographyLookupService rather than standing up real ones.
class ScanningFakeShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();
    public List<Guid> SortCalls { get; } = new();

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.TryGetValue(shipmentId, out var info) ? info : null);

    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.Values.FirstOrDefault(s => s.Awb == awb));

    public Task<ShipmentResponse?> GetResponseAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        SortCalls.Add(shipmentId);
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.Sorted };
        return Task.FromResult<ShipmentResponse?>(null);
    }

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

public class ManifestServiceScanningTests
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

    private static async Task<(ManifestService Service, ScanningFakeShipmentTransitionService Shipments, Guid ManifestId, Guid FromBranchId, Guid ToBranchId)> SeedDraftManifestAsync(TestDbContext dbContext, Guid organizationId)
    {
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var shipments = new ScanningFakeShipmentTransitionService();
        var service = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments,
            new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId));

        var manifest = await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId), ActingUser(Guid.NewGuid()));
        return (service, shipments, manifest.Id, fromBranchId, toBranchId);
    }

    private static ShipmentScanInfo AtOriginInfo(Guid fromBranchId, Guid toBranchId, string awb = "AWB001") => new(
        Guid.NewGuid(), Guid.NewGuid(), awb, ShipmentStatus.AtOriginBranch, fromBranchId, fromBranchId, toBranchId, null);

    [Fact]
    public async Task ScanAsync_ShipmentAtOriginBranch_AddsToManifestAndSortsShipment()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, fromBranchId, toBranchId) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());
        var info = AtOriginInfo(fromBranchId, toBranchId);
        shipments.ShipmentsById[info.ShipmentId] = info;

        var result = await service.ScanAsync(manifestId, new ScanShipmentRequest(info.Awb), ActingUser(Guid.NewGuid()));

        Assert.Equal(info.ShipmentId, result.ShipmentId);
        Assert.Equal(info.Awb, result.Awb);
        Assert.Null(result.ReceivedAt);
        Assert.Contains(info.ShipmentId, shipments.SortCalls);

        var manifest = await dbContext.Set<Manifest>().FirstAsync(m => m.Id == manifestId);
        Assert.Equal(1, manifest.ShipmentCount);
    }

    [Fact]
    public async Task ScanAsync_UnknownAwb_ThrowsWithShipmentNotFoundReasonCode()
    {
        await using var dbContext = CreateContext();
        var (service, _, manifestId, _, _) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ManifestScanRejectedException>(() =>
            service.ScanAsync(manifestId, new ScanShipmentRequest("NOSUCHAWB"), ActingUser(Guid.NewGuid())));
        Assert.Equal("SHIPMENT_NOT_FOUND", ex.ReasonCode);
    }

    [Fact]
    public async Task ScanAsync_ShipmentNotAtOriginBranch_ThrowsWithWrongStateReasonCode()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, fromBranchId, toBranchId) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());
        var info = AtOriginInfo(fromBranchId, toBranchId) with { CurrentStatus = ShipmentStatus.Created };
        shipments.ShipmentsById[info.ShipmentId] = info;

        var ex = await Assert.ThrowsAsync<ManifestScanRejectedException>(() =>
            service.ScanAsync(manifestId, new ScanShipmentRequest(info.Awb), ActingUser(Guid.NewGuid())));
        Assert.Equal("WRONG_STATE", ex.ReasonCode);
    }

    [Fact]
    public async Task ScanAsync_ShipmentAtDifferentBranch_ThrowsWithWrongCustodyReasonCode()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, fromBranchId, toBranchId) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());
        var info = AtOriginInfo(fromBranchId, toBranchId) with { CurrentBranchId = Guid.NewGuid() };
        shipments.ShipmentsById[info.ShipmentId] = info;

        var ex = await Assert.ThrowsAsync<ManifestScanRejectedException>(() =>
            service.ScanAsync(manifestId, new ScanShipmentRequest(info.Awb), ActingUser(Guid.NewGuid())));
        Assert.Equal("WRONG_CUSTODY", ex.ReasonCode);
    }

    [Fact]
    public async Task ScanAsync_ManifestAlreadySealed_ThrowsWithManifestNotOpenReasonCode()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, fromBranchId, toBranchId) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());
        var seedInfo = AtOriginInfo(fromBranchId, toBranchId);
        shipments.ShipmentsById[seedInfo.ShipmentId] = seedInfo;
        await service.ScanAsync(manifestId, new ScanShipmentRequest(seedInfo.Awb), ActingUser(Guid.NewGuid()));
        await service.SealAsync(manifestId, ActingUser(Guid.NewGuid()));

        var secondInfo = AtOriginInfo(fromBranchId, toBranchId, "AWB002");
        shipments.ShipmentsById[secondInfo.ShipmentId] = secondInfo;

        var ex = await Assert.ThrowsAsync<ManifestScanRejectedException>(() =>
            service.ScanAsync(manifestId, new ScanShipmentRequest(secondInfo.Awb), ActingUser(Guid.NewGuid())));
        Assert.Equal("MANIFEST_NOT_OPEN", ex.ReasonCode);
    }

    [Fact]
    public async Task ScanAsync_UnknownManifest_ThrowsManifestNotFoundException()
    {
        await using var dbContext = CreateContext();
        var shipments = new ScanningFakeShipmentTransitionService();
        var service = new ManifestService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), shipments,
            new AllBranchesAuthorizationChecker(), new FakeBranchLookup());

        await Assert.ThrowsAsync<ManifestNotFoundException>(() =>
            service.ScanAsync(Guid.NewGuid(), new ScanShipmentRequest("AWB001"), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task SealAsync_DraftManifestWithShipments_MovesToSealed()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, fromBranchId, toBranchId) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());
        var info = AtOriginInfo(fromBranchId, toBranchId);
        shipments.ShipmentsById[info.ShipmentId] = info;
        await service.ScanAsync(manifestId, new ScanShipmentRequest(info.Awb), ActingUser(Guid.NewGuid()));

        var result = await service.SealAsync(manifestId, ActingUser(Guid.NewGuid()));

        Assert.Equal(ManifestStatus.Sealed, result.Status);
    }

    [Fact]
    public async Task SealAsync_NoShipmentsScanned_Throws()
    {
        await using var dbContext = CreateContext();
        var (service, _, manifestId, _, _) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<ManifestValidationException>(() => service.SealAsync(manifestId, ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task ScanAsync_InboundInternationalReceivedFromCarrier_IsSorted()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, fromBranchId, toBranchId) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());
        var info = AtOriginInfo(fromBranchId, toBranchId) with { CurrentStatus = ShipmentStatus.ReceivedFromCarrier, Direction = ShipmentDirection.Inbound };
        shipments.ShipmentsById[info.ShipmentId] = info;

        var result = await service.ScanAsync(manifestId, new ScanShipmentRequest(info.Awb), ActingUser(Guid.NewGuid()));

        Assert.Equal(info.ShipmentId, result.ShipmentId);
        Assert.Contains(info.ShipmentId, shipments.SortCalls);
    }

    [Theory]
    [InlineData(ShipmentDirection.Outbound)]
    [InlineData(ShipmentDirection.Domestic)]
    public async Task ScanAsync_ReceivedFromCarrierButNotInbound_ThrowsWrongState(ShipmentDirection direction)
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, fromBranchId, toBranchId) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());
        var info = AtOriginInfo(fromBranchId, toBranchId) with { CurrentStatus = ShipmentStatus.ReceivedFromCarrier, Direction = direction };
        shipments.ShipmentsById[info.ShipmentId] = info;

        var ex = await Assert.ThrowsAsync<ManifestScanRejectedException>(() =>
            service.ScanAsync(manifestId, new ScanShipmentRequest(info.Awb), ActingUser(Guid.NewGuid())));
        Assert.Equal("WRONG_STATE", ex.ReasonCode);
    }
}
