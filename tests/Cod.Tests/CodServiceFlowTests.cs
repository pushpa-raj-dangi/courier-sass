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

file class ScopedBranchAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid>? _accessible;
    public ScopedBranchAuthorizationChecker(params Guid[] accessible) => _accessible = accessible.Length == 0 ? null : accessible.ToHashSet();
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessible);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessible is null || _accessible.Contains(branchId));
}

file class NoBranchLookup : IBranchLookupService
{
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

file class NoMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class NoAwbLookup : IShipmentAwbLookupService
{
    public Task<IReadOnlyDictionary<Guid, string>> GetAwbsByShipmentIdsAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class NoopOutboxWriter : IOutboxWriter
{
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class CodServiceFlowTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal Staff() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId, IBranchAuthorizationChecker checker) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NoBranchLookup(),
        checker, new NoMerchantLookup(), new NoAwbLookup(), new NoopOutboxWriter(), new FixedTimeZoneLookup(), new ZeroDeliveryLookup());

    private static void Seed(TestDbContext dbContext, Guid organizationId, Guid branchId, CodCollectionStatus status, decimal expected,
        decimal? collected = null, Guid? riderId = null, DateTimeOffset? createdAt = null, DateTimeOffset? collectedAt = null)
    {
        dbContext.Set<CODCollection>().Add(new CODCollection
        {
            Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = organizationId, MerchantId = Guid.NewGuid(),
            BranchId = branchId, ExpectedAmount = expected, ShipmentTotalCharge = 150m, CollectedAmount = collected,
            CollectedByRiderId = riderId, CollectedAt = collected is null ? null : collectedAt ?? DateTimeOffset.UtcNow,
            Status = status, CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        });
    }

    [Fact]
    public async Task GetFlowAsync_StagesAreCumulativeAndNonIncreasing()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var branch = Guid.NewGuid();
        Seed(db, org, branch, CodCollectionStatus.CodExpected, 1000m);
        Seed(db, org, branch, CodCollectionStatus.CodCollected, 2000m, 2000m, Guid.NewGuid());
        Seed(db, org, branch, CodCollectionStatus.RiderDeposited, 3000m, 3000m, Guid.NewGuid());
        Seed(db, org, branch, CodCollectionStatus.BranchReconciled, 4000m, 4000m, Guid.NewGuid());
        Seed(db, org, branch, CodCollectionStatus.FinanceVerified, 5000m, 5000m, Guid.NewGuid());
        Seed(db, org, branch, CodCollectionStatus.MerchantSettlementCreated, 6000m, 6000m, Guid.NewGuid());
        Seed(db, org, branch, CodCollectionStatus.Settled, 7000m, 6900m, Guid.NewGuid());
        await db.SaveChangesAsync();

        var flow = await CreateService(db, org, new ScopedBranchAuthorizationChecker()).GetFlowAsync(null, null, null, Staff());

        Assert.Equal(new[] { "expected", "collected", "riderDeposited", "branchReconciled", "financeVerified", "settled" }, flow.Stages.Select(s => s.Key));
        Assert.Equal(new[] { 28000m, 26900m, 24900m, 21900m, 17900m, 6900m }, flow.Stages.Select(s => s.Amount));
        Assert.Equal(new[] { 7, 6, 5, 4, 3, 1 }, flow.Stages.Select(s => s.Count));
    }

    [Fact]
    public async Task GetFlowAsync_Overage_ReportsExpectedBelowCollected()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        Seed(db, org, Guid.NewGuid(), CodCollectionStatus.CodCollected, 1000m, 1200m, Guid.NewGuid());
        await db.SaveChangesAsync();

        var flow = await CreateService(db, org, new ScopedBranchAuthorizationChecker()).GetFlowAsync(null, null, null, Staff());

        Assert.Equal(1000m, flow.Stages[0].Amount);
        Assert.Equal(1200m, flow.Stages[1].Amount);
        Assert.Equal(1, flow.Stages[0].Count);
        Assert.Equal(1, flow.Stages[1].Count);
    }

    [Fact]
    public async Task GetFlowAsync_DepositedRowWithNullCollectedAmount_StillCountsAsCollected()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        Seed(db, org, Guid.NewGuid(), CodCollectionStatus.RiderDeposited, 1000m);
        await db.SaveChangesAsync();

        var flow = await CreateService(db, org, new ScopedBranchAuthorizationChecker()).GetFlowAsync(null, null, null, Staff());

        Assert.Equal(new[] { 1, 1, 1, 0, 0, 0 }, flow.Stages.Select(s => s.Count));
    }

    [Fact]
    public async Task GetFlowAsync_EmptyScope_ReturnsSixZeroStages()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        Seed(db, org, Guid.NewGuid(), CodCollectionStatus.CodExpected, 1000m);
        Seed(db, Guid.NewGuid(), Guid.NewGuid(), CodCollectionStatus.CodExpected, 9999m); // other tenant
        await db.SaveChangesAsync();

        var flow = await CreateService(db, org, new ScopedBranchAuthorizationChecker()).GetFlowAsync(Guid.NewGuid(), null, null, Staff());

        Assert.Equal(6, flow.Stages.Count);
        Assert.All(flow.Stages, s => { Assert.Equal(0m, s.Amount); Assert.Equal(0, s.Count); });
    }

    [Fact]
    public async Task GetFlowAsync_RespectsBranchScope_AndCreatedAtRange()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        Seed(db, org, mine, CodCollectionStatus.CodExpected, 1000m, createdAt: new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero));
        Seed(db, org, mine, CodCollectionStatus.CodExpected, 500m, createdAt: new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));
        Seed(db, org, other, CodCollectionStatus.CodExpected, 7000m, createdAt: new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero));
        await db.SaveChangesAsync();

        var flow = await CreateService(db, org, new ScopedBranchAuthorizationChecker(mine)).GetFlowAsync(null,
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 7, 23, 59, 59, TimeSpan.Zero), Staff());

        Assert.Equal(1000m, flow.Stages[0].Amount);
        Assert.Equal(1, flow.Stages[0].Count);
    }

    [Fact]
    public async Task GetFlowAsync_FromAfterTo_Throws()
    {
        await using var db = CreateContext();
        await Assert.ThrowsAsync<CodValidationException>(() => CreateService(db, Guid.NewGuid(), new ScopedBranchAuthorizationChecker())
            .GetFlowAsync(null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1), Staff()));
    }

    [Fact]
    public async Task GetRiderCashAsync_GroupsUndepositedCashByRiderAndBranch_OldestFirst()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var riderA = Guid.NewGuid();
        var riderB = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        Seed(db, org, branch, CodCollectionStatus.CodCollected, 1000m, 1000m, riderA, collectedAt: now.AddHours(-2));
        Seed(db, org, branch, CodCollectionStatus.CodCollected, 1500m, 1500m, riderA, collectedAt: now.AddHours(-1));
        Seed(db, org, branch, CodCollectionStatus.CodCollected, 800m, 800m, riderB, collectedAt: now.AddHours(-30));
        Seed(db, org, branch, CodCollectionStatus.RiderDeposited, 9000m, 9000m, riderA, collectedAt: now.AddHours(-50)); // already deposited
        Seed(db, org, branch, CodCollectionStatus.CodExpected, 400m); // not collected
        await db.SaveChangesAsync();

        var cash = await CreateService(db, org, new ScopedBranchAuthorizationChecker()).GetRiderCashAsync(null, Staff());

        Assert.Equal(2, cash.Items.Count);
        Assert.Equal(riderB, cash.Items[0].RiderId);
        Assert.Equal(800m, cash.Items[0].Amount);
        var a = cash.Items[1];
        Assert.Equal(riderA, a.RiderId);
        Assert.Equal(2, a.ParcelCount);
        Assert.Equal(2500m, a.Amount);
        Assert.True(a.OldestCollectedAt < a.LatestCollectedAt);
    }

    [Fact]
    public async Task GetRiderCashAsync_ExcludesOutOfScopeBranches()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var mine = Guid.NewGuid();
        Seed(db, org, mine, CodCollectionStatus.CodCollected, 1000m, 1000m, Guid.NewGuid());
        Seed(db, org, Guid.NewGuid(), CodCollectionStatus.CodCollected, 2000m, 2000m, Guid.NewGuid());
        await db.SaveChangesAsync();

        var cash = await CreateService(db, org, new ScopedBranchAuthorizationChecker(mine)).GetRiderCashAsync(null, Staff());

        var single = Assert.Single(cash.Items);
        Assert.Equal(mine, single.BranchId);
    }

    [Fact]
    public async Task GetRiderCashAsync_ExcludesOtherTenant()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var rider = Guid.NewGuid();
        Seed(db, org, branch, CodCollectionStatus.CodCollected, 1000m, 1000m, rider);
        Seed(db, Guid.NewGuid(), branch, CodCollectionStatus.CodCollected, 9999m, 9999m, rider); // other tenant, same ids
        await db.SaveChangesAsync();

        var cash = await CreateService(db, org, new ScopedBranchAuthorizationChecker()).GetRiderCashAsync(null, Staff());

        var single = Assert.Single(cash.Items);
        Assert.Equal(1000m, single.Amount);
        Assert.Equal(1, single.ParcelCount);
    }

    [Fact]
    public async Task GetRiderCashAsync_ExcludesCollectionsWithoutRider()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var rider = Guid.NewGuid();
        Seed(db, org, branch, CodCollectionStatus.CodCollected, 1000m, 1000m, rider);
        Seed(db, org, branch, CodCollectionStatus.CodCollected, 500m, 500m, riderId: null);
        await db.SaveChangesAsync();

        var cash = await CreateService(db, org, new ScopedBranchAuthorizationChecker()).GetRiderCashAsync(null, Staff());

        var single = Assert.Single(cash.Items);
        Assert.Equal(rider, single.RiderId);
        Assert.Equal(1000m, single.Amount);
    }

    // Held amount must equal what CreateRiderSettlementAsync sweeps (every CodCollected row for the
    // rider at the branch), including a row whose CollectedAt is missing — its CreatedAt stands in.
    [Fact]
    public async Task GetRiderCashAsync_IncludesRowsWithNullCollectedAt_FallingBackToCreatedAt()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var rider = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var createdLongAgo = now.AddHours(-40);
        Seed(db, org, branch, CodCollectionStatus.CodCollected, 1000m, 1000m, rider, collectedAt: now.AddHours(-2));
        db.Set<CODCollection>().Add(new CODCollection
        {
            Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = org, MerchantId = Guid.NewGuid(),
            BranchId = branch, ExpectedAmount = 700m, ShipmentTotalCharge = 150m, CollectedAmount = 700m,
            CollectedByRiderId = rider, CollectedAt = null, Status = CodCollectionStatus.CodCollected, CreatedAt = createdLongAgo
        });
        await db.SaveChangesAsync();

        var cash = await CreateService(db, org, new ScopedBranchAuthorizationChecker()).GetRiderCashAsync(null, Staff());

        var single = Assert.Single(cash.Items);
        Assert.Equal(2, single.ParcelCount);
        Assert.Equal(1700m, single.Amount);
        Assert.Equal(createdLongAgo, single.OldestCollectedAt);
        Assert.Equal(now.AddHours(-2), single.LatestCollectedAt);
    }

    [Fact]
    public async Task GetRiderCashAsync_TiesOnOldestBreakByRiderThenBranch()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow.AddHours(-3);
        var riders = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var branches = Enumerable.Range(0, 2).Select(_ => Guid.NewGuid()).ToList();
        foreach (var rider in riders)
        {
            foreach (var branch in branches)
            {
                Seed(db, org, branch, CodCollectionStatus.CodCollected, 100m, 100m, rider, collectedAt: at);
            }
        }
        await db.SaveChangesAsync();

        var cash = await CreateService(db, org, new ScopedBranchAuthorizationChecker()).GetRiderCashAsync(null, Staff());

        var expected = riders.SelectMany(r => branches.Select(b => (Rider: r, Branch: b)))
            .OrderBy(x => x.Rider).ThenBy(x => x.Branch).ToList();
        Assert.Equal(expected, cash.Items.Select(i => (Rider: i.RiderId, Branch: i.BranchId)).ToList());
    }
}
