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

        var result = await listService.ListAsync(null, null, ActingUser(Guid.NewGuid()));

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

        var result = await listService.ListAsync(null, ManifestStatus.Sealed, ActingUser(Guid.NewGuid()));

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

        var result = await listService.ListAsync(branchA, null, ActingUser(Guid.NewGuid()));

        var resultIds = result.Select(m => m.Id).ToList();
        Assert.Contains(manifestAB.Id, resultIds);
        Assert.Single(result);
    }
}
