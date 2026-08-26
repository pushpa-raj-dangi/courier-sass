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

public class CodServiceAdjustmentsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MerchantSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantSettlementItemConfiguration());
            modelBuilder.ApplyConfiguration(new AdjustmentConfiguration());
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

    private static async Task<(MerchantSettlement Settlement, MerchantSettlementItem Item)> SeedSettlementItemAsync(TestDbContext dbContext, Guid organizationId)
    {
        var settlement = new MerchantSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, MerchantId = Guid.NewGuid(),
            PeriodFrom = DateTimeOffset.UtcNow.AddDays(-7), PeriodTo = DateTimeOffset.UtcNow,
            TotalPayable = 2350m, Status = MerchantSettlementStatus.Created
        };
        dbContext.Set<MerchantSettlement>().Add(settlement);
        var item = new MerchantSettlementItem
        {
            Id = Guid.NewGuid(), MerchantSettlementId = settlement.Id, ShipmentId = Guid.NewGuid(),
            CODAmount = 2500m, CourierFee = 150m, OtherDeductions = 0m, NetPayable = 2350m
        };
        dbContext.Set<MerchantSettlementItem>().Add(item);
        await dbContext.SaveChangesAsync();
        return (settlement, item);
    }

    [Fact]
    public async Task RequestAdjustmentAsync_CapturesCurrentOtherDeductionsAsBeforeValue()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (_, item) = await SeedSettlementItemAsync(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId);

        var response = await service.RequestAdjustmentAsync(
            new RequestAdjustmentRequest("MerchantSettlementItem", item.Id, 100m, "Damaged parcel deduction"), ActingUser(Guid.NewGuid()));

        Assert.Equal(0m, response.BeforeValue);
        Assert.Equal(100m, response.AfterValue);
        Assert.False(response.Applied);
        var unchangedItem = await dbContext.Set<MerchantSettlementItem>().SingleAsync(i => i.Id == item.Id);
        Assert.Equal(0m, unchangedItem.OtherDeductions);
    }

    [Fact]
    public async Task RequestAdjustmentAsync_UnsupportedEntityType_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.RequestAdjustmentAsync(new RequestAdjustmentRequest("Shipment", Guid.NewGuid(), 50m, "n/a"), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task ApproveAdjustmentAsync_RewritesOtherDeductionsAndRecomputesNetPayableAndTotalPayable()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (settlement, item) = await SeedSettlementItemAsync(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId);
        var requested = await service.RequestAdjustmentAsync(
            new RequestAdjustmentRequest("MerchantSettlementItem", item.Id, 100m, "Damaged parcel deduction"), ActingUser(Guid.NewGuid()));

        var approved = await service.ApproveAdjustmentAsync(requested.Id, ActingUser(Guid.NewGuid()));

        Assert.True(approved!.Applied);
        Assert.NotNull(approved.ApprovedAt);
        var updatedItem = await dbContext.Set<MerchantSettlementItem>().SingleAsync(i => i.Id == item.Id);
        Assert.Equal(100m, updatedItem.OtherDeductions);
        Assert.Equal(2250m, updatedItem.NetPayable); // 2500 - 150 - 100
        var updatedSettlement = await dbContext.Set<MerchantSettlement>().SingleAsync(m => m.Id == settlement.Id);
        Assert.Equal(2250m, updatedSettlement.TotalPayable);
    }

    [Fact]
    public async Task ApproveAdjustmentAsync_AlreadyApplied_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (_, item) = await SeedSettlementItemAsync(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId);
        var requested = await service.RequestAdjustmentAsync(
            new RequestAdjustmentRequest("MerchantSettlementItem", item.Id, 100m, "Damaged parcel deduction"), ActingUser(Guid.NewGuid()));
        await service.ApproveAdjustmentAsync(requested.Id, ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<CodValidationException>(() => service.ApproveAdjustmentAsync(requested.Id, ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task RequestAdjustmentAsync_ItemBelongsToDifferentOrganization_Throws()
    {
        await using var dbContext = CreateContext();
        var ownOrganizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();
        var (_, otherOrgItem) = await SeedSettlementItemAsync(dbContext, otherOrganizationId);
        var service = CreateService(dbContext, ownOrganizationId);

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.RequestAdjustmentAsync(
                new RequestAdjustmentRequest("MerchantSettlementItem", otherOrgItem.Id, 100m, "Damaged parcel deduction"), ActingUser(Guid.NewGuid())));

        var unchangedItem = await dbContext.Set<MerchantSettlementItem>().SingleAsync(i => i.Id == otherOrgItem.Id);
        Assert.Equal(0m, unchangedItem.OtherDeductions);
    }

    [Fact]
    public async Task ApproveAdjustmentAsync_AdjustmentBelongsToDifferentOrganization_ReturnsNullAndDoesNotApply()
    {
        await using var dbContext = CreateContext();
        var ownerOrganizationId = Guid.NewGuid();
        var attackerOrganizationId = Guid.NewGuid();
        var (_, item) = await SeedSettlementItemAsync(dbContext, ownerOrganizationId);
        var ownerService = CreateService(dbContext, ownerOrganizationId);
        var requested = await ownerService.RequestAdjustmentAsync(
            new RequestAdjustmentRequest("MerchantSettlementItem", item.Id, 100m, "Damaged parcel deduction"), ActingUser(Guid.NewGuid()));

        var attackerService = CreateService(dbContext, attackerOrganizationId);
        var result = await attackerService.ApproveAdjustmentAsync(requested.Id, ActingUser(Guid.NewGuid()));

        Assert.Null(result);
        var unchangedItem = await dbContext.Set<MerchantSettlementItem>().SingleAsync(i => i.Id == item.Id);
        Assert.Equal(0m, unchangedItem.OtherDeductions);
        var unchangedAdjustment = await dbContext.Set<Adjustment>().SingleAsync(a => a.Id == requested.Id);
        Assert.False(unchangedAdjustment.Applied);
    }
}
