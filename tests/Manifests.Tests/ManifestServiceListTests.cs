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

file class RestrictedBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid> _accessible;
    public RestrictedBranchesAuthorizationChecker(params Guid[] accessibleBranchIds) => _accessible = accessibleBranchIds.ToHashSet();
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(_accessible);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessible.Contains(branchId));
}

file class StubShipmentTransitionService : IShipmentTransitionService
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

public class ManifestServiceListTests
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

    private static async Task<ManifestResponse> CreateManifestAsync(TestDbContext dbContext, Guid organizationId, Guid fromBranchId, Guid toBranchId)
    {
        var service = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(),
            new StubShipmentTransitionService(), new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId), new FakeShipmentReportingLookup());
        return await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId), ActingUser(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListAsync_ExcludesManifestsOutsideCallersAccessibleBranches()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var accessibleBranchId1 = Guid.NewGuid();
        var accessibleBranchId2 = Guid.NewGuid();
        var inaccessibleBranchId1 = Guid.NewGuid();
        var inaccessibleBranchId2 = Guid.NewGuid();

        var accessibleManifest = await CreateManifestAsync(dbContext, organizationId, accessibleBranchId1, accessibleBranchId2);
        await CreateManifestAsync(dbContext, organizationId, inaccessibleBranchId1, inaccessibleBranchId2);

        var listService = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(),
            new StubShipmentTransitionService(), new RestrictedBranchesAuthorizationChecker(accessibleBranchId1, accessibleBranchId2),
            new FakeBranchLookup(accessibleBranchId1, accessibleBranchId2), new FakeShipmentReportingLookup());

        var result = await listService.ListAsync(null, null, null, ActingUser(Guid.NewGuid()));

        var resultIds = result.Select(m => m.Id).ToList();
        Assert.Contains(accessibleManifest.Id, resultIds);
        Assert.Single(result);
    }

    [Fact]
    public async Task ListAsync_FiltersByStatus()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();

        var draftManifest = await CreateManifestAsync(dbContext, organizationId, fromBranchId, toBranchId);
        var sealedManifest = await CreateManifestAsync(dbContext, organizationId, fromBranchId, toBranchId);
        var sealedEntity = await dbContext.Set<Manifest>().FirstAsync(m => m.Id == sealedManifest.Id);
        sealedEntity.Status = ManifestStatus.Sealed;
        await dbContext.SaveChangesAsync();

        var listService = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(),
            new StubShipmentTransitionService(), new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId), new FakeShipmentReportingLookup());

        var result = await listService.ListAsync(null, ManifestStatus.Sealed, null, ActingUser(Guid.NewGuid()));

        var resultIds = result.Select(m => m.Id).ToList();
        Assert.Contains(sealedManifest.Id, resultIds);
        Assert.DoesNotContain(draftManifest.Id, resultIds);
    }

    [Fact]
    public async Task ListAsync_FiltersByBranchId()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var branchC = Guid.NewGuid();
        var branchD = Guid.NewGuid();

        var manifestAB = await CreateManifestAsync(dbContext, organizationId, branchA, branchB);
        await CreateManifestAsync(dbContext, organizationId, branchC, branchD);

        var listService = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(),
            new StubShipmentTransitionService(), new AllBranchesAuthorizationChecker(), new FakeBranchLookup(branchA, branchB), new FakeShipmentReportingLookup());

        var result = await listService.ListAsync(branchA, null, null, ActingUser(Guid.NewGuid()));

        var resultIds = result.Select(m => m.Id).ToList();
        Assert.Contains(manifestAB.Id, resultIds);
        Assert.Single(result);
    }

    private static ManifestService ListService(TestDbContext dbContext, Guid organizationId, IBranchAuthorizationChecker checker) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new StubShipmentTransitionService(), checker,
            new FakeBranchLookup(), new FakeShipmentReportingLookup());

    [Fact]
    public async Task ListAsync_DirectionWithBranch_SplitsIncomingFromOutgoing()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var intoB = await CreateManifestAsync(dbContext, organizationId, a, b);
        var outOfB = await CreateManifestAsync(dbContext, organizationId, b, c);
        var service = ListService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var incoming = await service.ListAsync(b, null, "incoming", ActingUser(Guid.NewGuid()));
        var outgoing = await service.ListAsync(b, null, "outgoing", ActingUser(Guid.NewGuid()));
        var either = await service.ListAsync(b, null, null, ActingUser(Guid.NewGuid()));

        Assert.Equal(intoB.Id, Assert.Single(incoming).Id);
        Assert.Equal(outOfB.Id, Assert.Single(outgoing).Id);
        Assert.Equal(2, either.Count);
    }

    [Fact]
    public async Task ListAsync_DirectionWithoutBranch_UsesCallerScope()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var intoB = await CreateManifestAsync(dbContext, organizationId, a, b);
        var outOfB = await CreateManifestAsync(dbContext, organizationId, b, c);
        var service = ListService(dbContext, organizationId, new RestrictedBranchesAuthorizationChecker(b));

        var incoming = await service.ListAsync(null, null, "incoming", ActingUser(Guid.NewGuid()));
        var outgoing = await service.ListAsync(null, null, "OUTGOING", ActingUser(Guid.NewGuid()));

        Assert.Equal(intoB.Id, Assert.Single(incoming).Id);
        Assert.Equal(outOfB.Id, Assert.Single(outgoing).Id);
    }

    [Fact]
    public async Task ListAsync_DirectionAllBranchesScopeNoBranch_DoesNotRestrict()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        await CreateManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        await CreateManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        var service = ListService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var incoming = await service.ListAsync(null, null, "incoming", ActingUser(Guid.NewGuid()));

        Assert.Equal(2, incoming.Count);
    }

    [Fact]
    public async Task ListAsync_DirectionWithBranchOutsideScope_ReturnsEmpty()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await CreateManifestAsync(dbContext, organizationId, b, c);
        var service = ListService(dbContext, organizationId, new RestrictedBranchesAuthorizationChecker(a));

        var incoming = await service.ListAsync(c, null, "incoming", ActingUser(Guid.NewGuid()));

        Assert.Empty(incoming);
    }

    [Fact]
    public async Task ListAsync_InvalidDirection_Throws()
    {
        await using var dbContext = CreateContext();
        var service = ListService(dbContext, Guid.NewGuid(), new AllBranchesAuthorizationChecker());

        await Assert.ThrowsAsync<ManifestValidationException>(() => service.ListAsync(null, null, "sideways", ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task ListAsync_ReceivedCount_CountsReceivedParcelsPerManifest()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var withParcels = await CreateManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        var empty = await CreateManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        dbContext.Set<ManifestShipment>().AddRange(
            new ManifestShipment { Id = Guid.NewGuid(), ManifestId = withParcels.Id, ShipmentId = Guid.NewGuid(), ScannedAt = now, ReceivedAt = now },
            new ManifestShipment { Id = Guid.NewGuid(), ManifestId = withParcels.Id, ShipmentId = Guid.NewGuid(), ScannedAt = now, ReceivedAt = now },
            new ManifestShipment { Id = Guid.NewGuid(), ManifestId = withParcels.Id, ShipmentId = Guid.NewGuid(), ScannedAt = now, ReceivedAt = null });
        await dbContext.SaveChangesAsync();
        var service = ListService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListAsync(null, null, null, ActingUser(Guid.NewGuid()));

        Assert.Equal(2, result.Single(m => m.Id == withParcels.Id).ReceivedCount);
        Assert.Equal(0, result.Single(m => m.Id == empty.Id).ReceivedCount);
    }
}
