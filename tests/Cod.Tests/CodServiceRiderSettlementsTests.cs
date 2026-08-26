using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
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

file class AlwaysTrueBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public class CodServiceRiderSettlementsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
            modelBuilder.ApplyConfiguration(new RiderCashSettlementConfiguration());
        }
    }

    // Postgres-only feature (transactions are no-ops against the InMemory provider, which has no
    // real transaction log) — EF Core's InMemory provider silently accepts
    // BeginTransactionAsync/CommitAsync/RollbackAsync calls without erroring, so these tests run
    // unchanged against it; only the dedicated concurrency test in Task 14's Api.Tests suite proves
    // the transaction is real, against actual Postgres (mirroring Phase 5's rate-card race test
    // precedent).
    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        new AllBranchesAuthorizationChecker(), new AlwaysTrueMerchantLookup());

    private static async Task<CODCollection> SeedCollectedAsync(TestDbContext dbContext, Guid organizationId, Guid riderId, Guid branchId, decimal amount)
    {
        var collection = new CODCollection
        {
            Id = Guid.NewGuid(),
            ShipmentId = Guid.NewGuid(),
            OrganizationId = organizationId,
            MerchantId = Guid.NewGuid(),
            BranchId = branchId,
            ExpectedAmount = amount,
            ShipmentTotalCharge = 150m,
            CollectedAmount = amount,
            CollectedByRiderId = riderId,
            CollectedAt = DateTimeOffset.UtcNow,
            Status = CodCollectionStatus.CodCollected
        };
        dbContext.Set<CODCollection>().Add(collection);
        await dbContext.SaveChangesAsync();
        return collection;
    }

    [Fact]
    public async Task CreateRiderSettlementAsync_WithCollectedCod_BulkTransitionsToRiderDeposited()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var first = await SeedCollectedAsync(dbContext, organizationId, riderId, branchId, 2500m);
        var second = await SeedCollectedAsync(dbContext, organizationId, riderId, branchId, 1000m);
        var service = CreateService(dbContext, organizationId);

        var response = await service.CreateRiderSettlementAsync(new CreateRiderSettlementRequest(riderId, branchId, 3500m), ActingUser(Guid.NewGuid()));

        Assert.Equal(2, response.ShipmentCount);
        Assert.Equal(3500m, response.DeclaredAmount);
        var reloadedFirst = await dbContext.Set<CODCollection>().SingleAsync(c => c.Id == first.Id);
        var reloadedSecond = await dbContext.Set<CODCollection>().SingleAsync(c => c.Id == second.Id);
        Assert.Equal(CodCollectionStatus.RiderDeposited, reloadedFirst.Status);
        Assert.Equal(CodCollectionStatus.RiderDeposited, reloadedSecond.Status);
        Assert.Equal(response.Id, reloadedFirst.RiderCashSettlementId);
        Assert.Equal(response.Id, reloadedSecond.RiderCashSettlementId);
    }

    [Fact]
    public async Task CreateRiderSettlementAsync_NoCollectedCod_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.CreateRiderSettlementAsync(new CreateRiderSettlementRequest(Guid.NewGuid(), Guid.NewGuid(), 100m), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateRiderSettlementAsync_AlreadyDeposited_NotIncludedTwice()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await SeedCollectedAsync(dbContext, organizationId, riderId, branchId, 2500m);
        var service = CreateService(dbContext, organizationId);
        await service.CreateRiderSettlementAsync(new CreateRiderSettlementRequest(riderId, branchId, 2500m), ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.CreateRiderSettlementAsync(new CreateRiderSettlementRequest(riderId, branchId, 2500m), ActingUser(Guid.NewGuid())));
    }

    // Cross-tenant regression coverage — CodService is constructed against the generic DbContext
    // type (not concretely AppDbContext), so the production global HasQueryFilter on OrganizationId
    // is not guaranteed by the type system, and this test's TestDbContext above deliberately has no
    // such filter — so this can only pass if the CODCollection query in CreateRiderSettlementAsync
    // applies its own explicit OrganizationId filter (matching every other query in this module).
    [Fact]
    public async Task CreateRiderSettlementAsync_SameRiderAndBranchInDifferentOrganization_ExcludesForeignCollections()
    {
        await using var dbContext = CreateContext();
        var foreignOrganizationId = Guid.NewGuid();
        var callingOrganizationId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        // Same RiderId/BranchId, foreign organization — if the org filter were missing this would be
        // folded into the calling organization's settlement below.
        await SeedCollectedAsync(dbContext, foreignOrganizationId, riderId, branchId, 9999m);
        var service = CreateService(dbContext, callingOrganizationId);

        // No in-org collection exists for this rider/branch, so the call must throw rather than
        // silently settling the foreign organization's collection.
        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.CreateRiderSettlementAsync(new CreateRiderSettlementRequest(riderId, branchId, 9999m), ActingUser(Guid.NewGuid())));
    }
}
