using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
using Courier.Events.Application;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Cod.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class RestrictedBranchAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid> _accessibleBranchIds;
    public RestrictedBranchAuthorizationChecker(params Guid[] accessibleBranchIds) => _accessibleBranchIds = accessibleBranchIds.ToHashSet();
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(_accessibleBranchIds);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessibleBranchIds.Contains(branchId));
}

file class AlwaysTrueBranchLookup : IBranchLookupService
{
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class AlwaysTrueShipmentAwbLookup : IShipmentAwbLookupService
{
    public Task<IReadOnlyDictionary<Guid, string>> GetAwbsByShipmentIdsAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class NoopOutboxWriter : IOutboxWriter
{
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class CodServiceRiderSettlementsListTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RiderCashSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
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

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId, IBranchAuthorizationChecker branchAuthorizationChecker) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        branchAuthorizationChecker, new AlwaysTrueMerchantLookup(), new AlwaysTrueShipmentAwbLookup(), new NoopOutboxWriter());

    private static async Task<RiderCashSettlement> SeedAsync(TestDbContext dbContext, Guid organizationId, Guid riderId, Guid branchId, Guid? branchCashSettlementId = null, DateTimeOffset? settledAt = null, decimal declaredAmount = 2500m)
    {
        var settlement = new RiderCashSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, RiderId = riderId, BranchId = branchId,
            DeclaredAmount = declaredAmount, ShipmentCount = 1, SettledAt = settledAt ?? DateTimeOffset.UtcNow,
            ConfirmedByUserId = Guid.NewGuid(), BranchCashSettlementId = branchCashSettlementId
        };
        dbContext.Set<RiderCashSettlement>().Add(settlement);
        await dbContext.SaveChangesAsync();
        return settlement;
    }

    private static async Task SeedLinkedCollectionAsync(TestDbContext dbContext, Guid organizationId, Guid branchId, Guid riderCashSettlementId, decimal collectedAmount)
    {
        dbContext.Set<CODCollection>().Add(new CODCollection
        {
            Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = organizationId, MerchantId = Guid.NewGuid(),
            BranchId = branchId, ExpectedAmount = collectedAmount, ShipmentTotalCharge = 150m, CollectedAmount = collectedAmount,
            CollectedAt = DateTimeOffset.UtcNow, Status = CodCollectionStatus.RiderDeposited, RiderCashSettlementId = riderCashSettlementId
        });
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task ListRiderSettlementsAsync_ExcludesForeignOrganization()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        var own = await SeedAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        await SeedAsync(dbContext, foreignOrganizationId, Guid.NewGuid(), Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListRiderSettlementsAsync(null, null, null, null, null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(own.Id, single.Id);
    }

    [Fact]
    public async Task ListRiderSettlementsAsync_BranchRestricted_FiltersToAccessibleBranchOnly()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var accessibleBranchId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();
        var accessible = await SeedAsync(dbContext, organizationId, Guid.NewGuid(), accessibleBranchId);
        await SeedAsync(dbContext, organizationId, Guid.NewGuid(), otherBranchId);
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker(accessibleBranchId));

        var result = await service.ListRiderSettlementsAsync(null, null, null, null, null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(accessible.Id, single.Id);
    }

    [Fact]
    public async Task ListRiderSettlementsAsync_UnreconciledOnly_ExcludesRowsWithBranchCashSettlementId()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var unreconciled = await SeedAsync(dbContext, organizationId, Guid.NewGuid(), branchId);
        await SeedAsync(dbContext, organizationId, Guid.NewGuid(), branchId, branchCashSettlementId: Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListRiderSettlementsAsync(null, null, true, null, null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(unreconciled.Id, single.Id);
    }

    [Fact]
    public async Task ListRiderSettlementsAsync_FilteredByRiderId_ReturnsOnlyMatching()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var targetRiderId = Guid.NewGuid();
        var target = await SeedAsync(dbContext, organizationId, targetRiderId, Guid.NewGuid());
        await SeedAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListRiderSettlementsAsync(null, targetRiderId, null, null, null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(target.Id, single.Id);
    }

    [Fact]
    public async Task ListRiderSettlementsAsync_ComputesExpectedAndVariance()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var exact = await SeedAsync(dbContext, organizationId, Guid.NewGuid(), branchId, declaredAmount: 3000m);
        await SeedLinkedCollectionAsync(dbContext, organizationId, branchId, exact.Id, 1000m);
        await SeedLinkedCollectionAsync(dbContext, organizationId, branchId, exact.Id, 2000m);
        var shortSettlement = await SeedAsync(dbContext, organizationId, Guid.NewGuid(), branchId, declaredAmount: 2000m);
        await SeedLinkedCollectionAsync(dbContext, organizationId, branchId, shortSettlement.Id, 2500m);
        var over = await SeedAsync(dbContext, organizationId, Guid.NewGuid(), branchId, declaredAmount: 2700m);
        await SeedLinkedCollectionAsync(dbContext, organizationId, branchId, over.Id, 2500m);
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListRiderSettlementsAsync(null, null, null, null, null, null, null, ActingUser(Guid.NewGuid()));

        var byId = result.Items.ToDictionary(r => r.Id);
        Assert.Equal(3000m, byId[exact.Id].ExpectedAmount);
        Assert.Equal(0m, byId[exact.Id].Variance);
        Assert.Equal(2500m, byId[shortSettlement.Id].ExpectedAmount);
        Assert.Equal(-500m, byId[shortSettlement.Id].Variance);
        Assert.Equal(200m, byId[over.Id].Variance);
    }

    [Fact]
    public async Task ListRiderSettlementsAsync_SettlementWithNoLinkedCollections_ExpectedIsZero()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var legacy = await SeedAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid(), declaredAmount: 1200m);
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListRiderSettlementsAsync(null, null, null, null, null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(legacy.Id, single.Id);
        Assert.Equal(0m, single.ExpectedAmount);
        Assert.Equal(1200m, single.Variance);
    }

    [Fact]
    public async Task ListRiderSettlementsAsync_FromTo_FiltersOnSettledAt_NormalisedToUtc()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var inRange = await SeedAsync(dbContext, organizationId, Guid.NewGuid(), branchId, settledAt: new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        await SeedAsync(dbContext, organizationId, Guid.NewGuid(), branchId, settledAt: new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());
        var nepal = TimeSpan.FromMinutes(345);

        var result = await service.ListRiderSettlementsAsync(null, null, null,
            new DateTimeOffset(2026, 10, 5, 5, 45, 0, nepal), new DateTimeOffset(2026, 10, 6, 5, 44, 59, nepal),
            null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(inRange.Id, single.Id);
    }

    [Fact]
    public async Task ListRiderSettlementsAsync_FromAfterTo_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid(), new AllBranchesAuthorizationChecker());

        await Assert.ThrowsAsync<CodValidationException>(() => service.ListRiderSettlementsAsync(null, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1), null, null, ActingUser(Guid.NewGuid())));
    }
}
