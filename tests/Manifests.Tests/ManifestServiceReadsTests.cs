using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Manifests.Application;
using Courier.Manifests.Domain;
using Courier.Manifests.Infrastructure;
using Courier.Shipments.Application;
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

file class NotSupportedBranchLookup : IBranchLookupService
{
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class ScopedBranchesChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid>? _accessible;
    public ScopedBranchesChecker(IReadOnlySet<Guid>? accessible) => _accessible = accessible;
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessible);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessible is null || _accessible.Contains(branchId));
}

// Reads no longer touch the transition service (AWBs come from the batched reporting lookup), so
// every member throws.
file class NotSupportedShipmentTransitionService : IShipmentTransitionService
{
    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> GetResponseAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

public class ManifestServiceReadsTests
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

    private static ClaimsPrincipal ActingUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    private static ManifestService CreateService(TestDbContext dbContext, Guid organizationId, IReadOnlySet<Guid>? accessible, FakeShipmentReportingLookup lookup) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedShipmentTransitionService(),
            new ScopedBranchesChecker(accessible), new NotSupportedBranchLookup(), lookup);

    private static async Task<Manifest> SeedManifestAsync(TestDbContext dbContext, Guid organizationId, Guid fromBranchId, Guid toBranchId, ManifestStatus status = ManifestStatus.InTransit)
    {
        var manifest = new Manifest
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Code = $"MNF-20261015-{Random.Shared.Next(100, 999)}",
            FromBranchId = fromBranchId, ToBranchId = toBranchId, Status = status, ShipmentCount = 0,
            CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Set<Manifest>().Add(manifest);
        await dbContext.SaveChangesAsync();
        return manifest;
    }

    private static async Task<ManifestShipment> SeedParcelAsync(TestDbContext dbContext, Guid manifestId, DateTimeOffset scannedAt, DateTimeOffset? receivedAt = null)
    {
        var parcel = new ManifestShipment { Id = Guid.NewGuid(), ManifestId = manifestId, ShipmentId = Guid.NewGuid(), ScannedAt = scannedAt, ReceivedAt = receivedAt };
        dbContext.Set<ManifestShipment>().Add(parcel);
        await dbContext.SaveChangesAsync();
        return parcel;
    }

    [Fact]
    public async Task GetByIdAsync_ManifestOutsideReadScope_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new HashSet<Guid> { Guid.NewGuid() }, new FakeShipmentReportingLookup());

        Assert.Null(await service.GetByIdAsync(manifest.Id, ActingUser()));
    }

    [Fact]
    public async Task GetByIdAsync_ManifestToScopedBranch_ReturnsIt()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(dbContext, organizationId, Guid.NewGuid(), toBranchId);
        var service = CreateService(dbContext, organizationId, new HashSet<Guid> { toBranchId }, new FakeShipmentReportingLookup());

        var result = await service.GetByIdAsync(manifest.Id, ActingUser());

        Assert.NotNull(result);
        Assert.Equal(manifest.Id, result!.Id);
    }

    [Fact]
    public async Task GetMissingShipmentsAsync_ManifestOutsideReadScope_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new HashSet<Guid> { Guid.NewGuid() }, new FakeShipmentReportingLookup());

        Assert.Null(await service.GetMissingShipmentsAsync(manifest.Id, ActingUser()));
    }

    [Fact]
    public async Task GetMissingShipmentsAsync_ResolvesEveryAwbInOneBatchedLookup()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid(), ManifestStatus.Closed);
        var start = DateTimeOffset.UtcNow.AddHours(-5);
        var first = await SeedParcelAsync(dbContext, manifest.Id, start);
        var second = await SeedParcelAsync(dbContext, manifest.Id, start.AddMinutes(1));
        var third = await SeedParcelAsync(dbContext, manifest.Id, start.AddMinutes(2));
        await SeedParcelAsync(dbContext, manifest.Id, start.AddMinutes(3), receivedAt: DateTimeOffset.UtcNow);
        var lookup = new FakeShipmentReportingLookup();
        lookup.Awbs[first.ShipmentId] = "AWB-1";
        lookup.Awbs[second.ShipmentId] = "AWB-2";
        // third deliberately has no AWB: the response keeps the row with an empty Awb.
        var service = CreateService(dbContext, organizationId, null, lookup);

        var missing = await service.GetMissingShipmentsAsync(manifest.Id, ActingUser());

        Assert.NotNull(missing);
        Assert.Equal(new[] { "AWB-1", "AWB-2", string.Empty }, missing!.Select(m => m.Awb).ToArray());
        Assert.Equal(third.ShipmentId, missing[2].ShipmentId);
        Assert.Equal(1, lookup.AwbLookupCalls);
    }

    [Fact]
    public async Task ListShipmentsAsync_ReturnsEveryParcelOldestScanFirstWithAwbs()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid(), ManifestStatus.Received);
        var start = DateTimeOffset.UtcNow.AddHours(-3);
        var later = await SeedParcelAsync(dbContext, manifest.Id, start.AddMinutes(10), receivedAt: DateTimeOffset.UtcNow);
        var earlier = await SeedParcelAsync(dbContext, manifest.Id, start);
        var otherManifest = await SeedManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        await SeedParcelAsync(dbContext, otherManifest.Id, start);
        var lookup = new FakeShipmentReportingLookup();
        lookup.Awbs[later.ShipmentId] = "AWB-LATER";
        lookup.Awbs[earlier.ShipmentId] = "AWB-EARLIER";
        var service = CreateService(dbContext, organizationId, null, lookup);

        var parcels = await service.ListShipmentsAsync(manifest.Id, ActingUser());

        Assert.NotNull(parcels);
        Assert.Equal(new[] { "AWB-EARLIER", "AWB-LATER" }, parcels!.Select(p => p.Awb).ToArray());
        Assert.Null(parcels[0].ReceivedAt);
        Assert.NotNull(parcels[1].ReceivedAt);
        Assert.Equal(1, lookup.AwbLookupCalls);
    }

    [Fact]
    public async Task ListShipmentsAsync_ManifestOutsideReadScope_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new HashSet<Guid> { Guid.NewGuid() }, new FakeShipmentReportingLookup());

        Assert.Null(await service.ListShipmentsAsync(manifest.Id, ActingUser()));
    }

    [Fact]
    public async Task ListShipmentsAsync_UnknownManifest_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid(), null, new FakeShipmentReportingLookup());

        Assert.Null(await service.ListShipmentsAsync(Guid.NewGuid(), ActingUser()));
    }
}
