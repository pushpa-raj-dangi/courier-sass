using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Geography.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Tests;

public class CodReportingLookupServiceTests
{
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId { get; set; }
    }

    private static readonly Guid ProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid DistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid LocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");

    private static async Task<(Guid OrgId, Guid BranchId, Guid MerchantId)> SeedOrgAsync(Courier.Persistence.AppDbContext dbContext)
    {
        var orgId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        dbContext.Add(new Courier.Identity.Domain.Organization { Id = orgId, Name = $"Org {orgId}", Slug = $"org-{orgId:N}" });
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = branchId, OrganizationId = orgId, Code = "BR1", Name = "Branch One",
            Type = Courier.Branches.Domain.BranchType.Branch, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });
        dbContext.Add(new Courier.Merchants.Domain.Merchant { Id = merchantId, OrganizationId = orgId, LegalName = "Test Merchant Pvt Ltd", DisplayName = "Test Merchant" });
        await dbContext.SaveChangesAsync();
        return (orgId, branchId, merchantId);
    }

    [Fact]
    public async Task GetOrgFinancialSummaryAsync_ComputesExpectedCollectedPendingSettledMerchantPayable()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        dbContext.Add(new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, BranchId = branchId, ExpectedAmount = 500m, ShipmentTotalCharge = 100m, Status = CodCollectionStatus.CodExpected });
        dbContext.Add(new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, BranchId = branchId, ExpectedAmount = 300m, ShipmentTotalCharge = 100m, CollectedAmount = 300m, Status = CodCollectionStatus.Settled });
        var settlement = new MerchantSettlement { Id = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, PeriodFrom = DateTimeOffset.UtcNow.AddDays(-7), PeriodTo = DateTimeOffset.UtcNow, TotalPayable = 250m, Status = MerchantSettlementStatus.Settled, SettledAt = DateTimeOffset.UtcNow };
        var pendingSettlement = new MerchantSettlement { Id = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, PeriodFrom = DateTimeOffset.UtcNow.AddDays(-7), PeriodTo = DateTimeOffset.UtcNow, TotalPayable = 90m, Status = MerchantSettlementStatus.Created };
        dbContext.Add(settlement);
        dbContext.Add(pendingSettlement);
        await dbContext.SaveChangesAsync();

        var service = new CodReportingLookupService(dbContext);
        var result = await service.GetOrgFinancialSummaryAsync(null);

        Assert.Equal(500m, result.Expected);
        Assert.Equal(300m, result.Collected);
        Assert.Equal(0m, result.Pending);
        Assert.Equal(250m, result.Settled);
        Assert.Equal(90m, result.MerchantPayable);
    }

    [Fact]
    public async Task GetOrgFinancialSummaryAsync_ExcludesAnotherOrganizationsCollections()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgAId, branchA, merchantA) = await SeedOrgAsync(dbContext);
        var (orgBId, branchB, merchantB) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgAId;

        dbContext.Add(new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgAId, MerchantId = merchantA, BranchId = branchA, ExpectedAmount = 500m, ShipmentTotalCharge = 100m, Status = CodCollectionStatus.CodExpected });
        dbContext.Add(new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgBId, MerchantId = merchantB, BranchId = branchB, ExpectedAmount = 9000m, ShipmentTotalCharge = 100m, Status = CodCollectionStatus.CodExpected });
        await dbContext.SaveChangesAsync();

        var service = new CodReportingLookupService(dbContext);
        var result = await service.GetOrgFinancialSummaryAsync(null);

        Assert.Equal(500m, result.Expected);
    }

    [Fact]
    public async Task GetBranchPendingFinanceVerificationTotalAsync_SumsRiderDepositedAndBranchReconciled()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        dbContext.Add(new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, BranchId = branchId, ExpectedAmount = 200m, ShipmentTotalCharge = 50m, CollectedAmount = 200m, Status = CodCollectionStatus.RiderDeposited });
        dbContext.Add(new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, BranchId = branchId, ExpectedAmount = 150m, ShipmentTotalCharge = 50m, CollectedAmount = 150m, Status = CodCollectionStatus.BranchReconciled });
        dbContext.Add(new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, BranchId = branchId, ExpectedAmount = 999m, ShipmentTotalCharge = 50m, CollectedAmount = 999m, Status = CodCollectionStatus.Settled });
        await dbContext.SaveChangesAsync();

        var service = new CodReportingLookupService(dbContext);
        var total = await service.GetBranchPendingFinanceVerificationTotalAsync(branchId);

        Assert.Equal(350m, total);
    }

    [Fact]
    public async Task GetCodExportRowsAsync_IncludesUncollectedRows_ExcludesRowsCollectedOutsideRange()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var now = DateTimeOffset.UtcNow;
        var uncollected = new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, BranchId = branchId, ExpectedAmount = 400m, ShipmentTotalCharge = 50m, Status = CodCollectionStatus.CodExpected };
        var collectedInRange = new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, BranchId = branchId, ExpectedAmount = 400m, ShipmentTotalCharge = 50m, CollectedAmount = 400m, CollectedAt = now, Status = CodCollectionStatus.CodCollected };
        var collectedOutOfRange = new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, BranchId = branchId, ExpectedAmount = 400m, ShipmentTotalCharge = 50m, CollectedAmount = 400m, CollectedAt = now.AddDays(-10), Status = CodCollectionStatus.CodCollected };
        dbContext.Add(uncollected);
        dbContext.Add(collectedInRange);
        dbContext.Add(collectedOutOfRange);
        await dbContext.SaveChangesAsync();

        var service = new CodReportingLookupService(dbContext);
        var rows = await service.GetCodExportRowsAsync(null, now.AddHours(-1), now.AddHours(1));

        var shipmentIds = rows.Select(r => r.ShipmentId).ToHashSet();
        Assert.Contains(uncollected.ShipmentId, shipmentIds);
        Assert.Contains(collectedInRange.ShipmentId, shipmentIds);
        Assert.DoesNotContain(collectedOutOfRange.ShipmentId, shipmentIds);
    }

    [Fact]
    public async Task GetHeldByRiderAsync_SumsOnlyThisRidersUndepositedCollections()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        var (otherOrgId, otherBranchId, otherMerchantId) = await SeedOrgAsync(dbContext);
        var riderId = Guid.NewGuid();
        var oldest = DateTimeOffset.UtcNow.AddHours(-3);
        CODCollection Collection(Guid org, Guid branch, Guid merchant, Guid? rider, CodCollectionStatus status, decimal amount, DateTimeOffset? at) => new()
        {
            Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = org, MerchantId = merchant, BranchId = branch,
            ExpectedAmount = amount, ShipmentTotalCharge = 100m, CollectedAmount = amount, Status = status,
            CollectedByRiderId = rider, CollectedAt = at
        };
        dbContext.AddRange(
            Collection(orgId, branchId, merchantId, riderId, CodCollectionStatus.CodCollected, 1500m, oldest),
            Collection(orgId, branchId, merchantId, riderId, CodCollectionStatus.CodCollected, 850m, oldest.AddHours(2)),
            Collection(orgId, branchId, merchantId, riderId, CodCollectionStatus.RiderDeposited, 9000m, oldest.AddMinutes(-30)),
            Collection(orgId, branchId, merchantId, Guid.NewGuid(), CodCollectionStatus.CodCollected, 700m, oldest),
            Collection(otherOrgId, otherBranchId, otherMerchantId, riderId, CodCollectionStatus.CodCollected, 400m, oldest.AddHours(-5)));
        await dbContext.SaveChangesAsync();
        tenantProvider.OrganizationId = orgId;
        var service = new CodReportingLookupService(dbContext);

        var held = await service.GetHeldByRiderAsync(riderId);

        Assert.NotNull(held);
        Assert.Equal(2350m, held!.Amount);
        Assert.Equal(2, held.ParcelCount);
        Assert.Equal(oldest, held.OldestCollectedAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task GetHeldByRiderAsync_NothingHeld_ReturnsNull()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, _, _) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;
        var service = new CodReportingLookupService(dbContext);

        Assert.Null(await service.GetHeldByRiderAsync(Guid.NewGuid()));
    }
}
