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

public class CodServiceMerchantSettlementsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantSettlementItemConfiguration());
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

    private static async Task<CODCollection> SeedFinanceVerifiedAsync(TestDbContext dbContext, Guid organizationId, Guid merchantId, decimal codAmount, decimal totalCharge, DateTimeOffset collectedAt)
    {
        var collection = new CODCollection
        {
            Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = organizationId, MerchantId = merchantId,
            BranchId = Guid.NewGuid(), ExpectedAmount = codAmount, ShipmentTotalCharge = totalCharge, CollectedAmount = codAmount,
            CollectedAt = collectedAt, Status = CodCollectionStatus.FinanceVerified
        };
        dbContext.Set<CODCollection>().Add(collection);
        await dbContext.SaveChangesAsync();
        return collection;
    }

    [Fact]
    public async Task CreateMerchantSettlementAsync_MatchesWorkedExampleFromCodLifecycleDoc()
    {
        // COD-LIFECYCLE.md §2: COD Amount 2500, Courier Delivery Fee 150, Merchant Payable 2350.
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var periodFrom = DateTimeOffset.UtcNow.AddDays(-7);
        var periodTo = DateTimeOffset.UtcNow.AddDays(1);
        await SeedFinanceVerifiedAsync(dbContext, organizationId, merchantId, 2500m, 150m, DateTimeOffset.UtcNow);
        var service = CreateService(dbContext, organizationId);

        var response = await service.CreateMerchantSettlementAsync(new CreateMerchantSettlementRequest(merchantId, periodFrom, periodTo), ActingUser(Guid.NewGuid()));

        var item = Assert.Single(response.Items);
        Assert.Equal(2500m, item.CODAmount);
        Assert.Equal(150m, item.CourierFee);
        Assert.Equal(0m, item.OtherDeductions);
        Assert.Equal(2350m, item.NetPayable);
        Assert.Equal(2350m, response.TotalPayable);
        Assert.Equal(MerchantSettlementStatus.Created, response.Status);
    }

    [Fact]
    public async Task CreateMerchantSettlementAsync_OutsidePeriod_ExcludesCollection()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        await SeedFinanceVerifiedAsync(dbContext, organizationId, merchantId, 2500m, 150m, DateTimeOffset.UtcNow.AddDays(-30));
        var service = CreateService(dbContext, organizationId);

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.CreateMerchantSettlementAsync(new CreateMerchantSettlementRequest(merchantId, DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task SettleMerchantSettlementAsync_FromCreated_TransitionsCollectionsToSettled()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        await SeedFinanceVerifiedAsync(dbContext, organizationId, merchantId, 2500m, 150m, DateTimeOffset.UtcNow);
        var service = CreateService(dbContext, organizationId);
        var created = await service.CreateMerchantSettlementAsync(
            new CreateMerchantSettlementRequest(merchantId, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)), ActingUser(Guid.NewGuid()));

        var settled = await service.SettleMerchantSettlementAsync(created.Id, ActingUser(Guid.NewGuid()));

        Assert.Equal(MerchantSettlementStatus.Settled, settled!.Status);
        Assert.NotNull(settled.SettledAt);
        var collection = await dbContext.Set<CODCollection>().SingleAsync();
        Assert.Equal(CodCollectionStatus.Settled, collection.Status);
    }

    [Fact]
    public async Task SettleMerchantSettlementAsync_AlreadySettled_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        await SeedFinanceVerifiedAsync(dbContext, organizationId, merchantId, 2500m, 150m, DateTimeOffset.UtcNow);
        var service = CreateService(dbContext, organizationId);
        var created = await service.CreateMerchantSettlementAsync(
            new CreateMerchantSettlementRequest(merchantId, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)), ActingUser(Guid.NewGuid()));
        await service.SettleMerchantSettlementAsync(created.Id, ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<CodValidationException>(() => service.SettleMerchantSettlementAsync(created.Id, ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateMerchantSettlementAsync_ForDifferentOrganization_ExcludesOtherOrgsCollections()
    {
        // Regression for this task's central risk (see Task 9's review finding): a caller in one
        // organization must not be able to fold another organization's FinanceVerified COD
        // collections into a settlement merely by supplying a MerchantId/period that matches. The
        // merchant lookup fake always returns true (as a real cross-org merchant existence check is
        // out of this task's scope), so the only thing that can stop this is the OrganizationId
        // filter on the CODCollection query itself.
        await using var dbContext = CreateContext();
        var otherOrganizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        await SeedFinanceVerifiedAsync(dbContext, otherOrganizationId, merchantId, 2500m, 150m, DateTimeOffset.UtcNow);
        var callingOrganizationId = Guid.NewGuid();
        var service = CreateService(dbContext, callingOrganizationId);

        var exception = await Assert.ThrowsAsync<CodValidationException>(() =>
            service.CreateMerchantSettlementAsync(
                new CreateMerchantSettlementRequest(merchantId, DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow.AddDays(1)), ActingUser(Guid.NewGuid())));

        Assert.Equal("No finance-verified COD collections found for this merchant in this period.", exception.Message);
        var collection = await dbContext.Set<CODCollection>().SingleAsync();
        Assert.Equal(CodCollectionStatus.FinanceVerified, collection.Status);
        Assert.Null(collection.MerchantSettlementItemId);
    }

    [Fact]
    public async Task SettleMerchantSettlementAsync_ForDifferentOrganizationSettlement_ReturnsNull()
    {
        // Mirrors the cross-org regression above for the settle path: a MerchantSettlementId that
        // belongs to another organization must not be settleable, even by exact id.
        await using var dbContext = CreateContext();
        var ownerOrganizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        await SeedFinanceVerifiedAsync(dbContext, ownerOrganizationId, merchantId, 2500m, 150m, DateTimeOffset.UtcNow);
        var ownerService = CreateService(dbContext, ownerOrganizationId);
        var created = await ownerService.CreateMerchantSettlementAsync(
            new CreateMerchantSettlementRequest(merchantId, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)), ActingUser(Guid.NewGuid()));

        var attackerOrganizationId = Guid.NewGuid();
        var attackerService = CreateService(dbContext, attackerOrganizationId);
        var result = await attackerService.SettleMerchantSettlementAsync(created.Id, ActingUser(Guid.NewGuid()));

        Assert.Null(result);
        var collection = await dbContext.Set<CODCollection>().SingleAsync();
        Assert.Equal(CodCollectionStatus.MerchantSettlementCreated, collection.Status);
    }
}
