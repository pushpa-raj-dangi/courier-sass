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

public class CodServiceBranchSettlementsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
            modelBuilder.ApplyConfiguration(new RiderCashSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new BranchCashSettlementConfiguration());
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

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        new AllBranchesAuthorizationChecker(), new AlwaysTrueMerchantLookup());

    private static async Task<RiderCashSettlement> SeedRiderDepositedAsync(TestDbContext dbContext, Guid organizationId, Guid branchId, decimal amount)
    {
        var settlement = new RiderCashSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, RiderId = Guid.NewGuid(), BranchId = branchId,
            DeclaredAmount = amount, ShipmentCount = 1, SettledAt = DateTimeOffset.UtcNow, ConfirmedByUserId = Guid.NewGuid()
        };
        dbContext.Set<RiderCashSettlement>().Add(settlement);
        dbContext.Set<CODCollection>().Add(new CODCollection
        {
            Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = organizationId, MerchantId = Guid.NewGuid(),
            BranchId = branchId, ExpectedAmount = amount, ShipmentTotalCharge = 150m, CollectedAmount = amount,
            Status = CodCollectionStatus.RiderDeposited, RiderCashSettlementId = settlement.Id
        });
        await dbContext.SaveChangesAsync();
        return settlement;
    }

    [Fact]
    public async Task CreateBranchSettlementAsync_WithRiderDeposits_BulkTransitionsToBranchReconciled()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var riderSettlement = await SeedRiderDepositedAsync(dbContext, organizationId, branchId, 2500m);
        var service = CreateService(dbContext, organizationId);

        var response = await service.CreateBranchSettlementAsync(
            new CreateBranchSettlementRequest(branchId, new List<Guid> { riderSettlement.Id }, 2500m), ActingUser(Guid.NewGuid()));

        Assert.Equal(new List<Guid> { riderSettlement.Id }, response.RiderCashSettlementIds);
        var reloadedRiderSettlement = await dbContext.Set<RiderCashSettlement>().SingleAsync(r => r.Id == riderSettlement.Id);
        Assert.Equal(response.Id, reloadedRiderSettlement.BranchCashSettlementId);
        var collection = await dbContext.Set<CODCollection>().SingleAsync(c => c.RiderCashSettlementId == riderSettlement.Id);
        Assert.Equal(CodCollectionStatus.BranchReconciled, collection.Status);
    }

    [Fact]
    public async Task CreateBranchSettlementAsync_UnknownRiderCashSettlementId_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.CreateBranchSettlementAsync(new CreateBranchSettlementRequest(Guid.NewGuid(), new List<Guid> { Guid.NewGuid() }, 100m), ActingUser(Guid.NewGuid())));
    }

    // Cross-tenant regression coverage — CodService is constructed against the generic DbContext
    // type (not concretely AppDbContext), so the production global HasQueryFilter on OrganizationId
    // is not guaranteed by the type system, and this test's TestDbContext above deliberately has no
    // such filter — so this can only pass if the RiderCashSettlement lookup in
    // CreateBranchSettlementAsync applies its own explicit OrganizationId filter (matching every
    // other query in this module).
    [Fact]
    public async Task CreateBranchSettlementAsync_RiderCashSettlementBelongsToDifferentOrganization_Throws()
    {
        await using var dbContext = CreateContext();
        var foreignOrganizationId = Guid.NewGuid();
        var callingOrganizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        // Same BranchId, foreign organization — if the org filter were missing this would be folded
        // into the calling organization's branch settlement below.
        var foreignRiderSettlement = await SeedRiderDepositedAsync(dbContext, foreignOrganizationId, branchId, 2500m);
        var service = CreateService(dbContext, callingOrganizationId);

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.CreateBranchSettlementAsync(
                new CreateBranchSettlementRequest(branchId, new List<Guid> { foreignRiderSettlement.Id }, 2500m), ActingUser(Guid.NewGuid())));

        var unchangedCollection = await dbContext.Set<CODCollection>().SingleAsync(c => c.RiderCashSettlementId == foreignRiderSettlement.Id);
        Assert.Equal(CodCollectionStatus.RiderDeposited, unchangedCollection.Status);
    }
}
