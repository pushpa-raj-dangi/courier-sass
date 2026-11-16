using Courier.Geography.Infrastructure;
using Courier.Ndr.Application;
using Courier.Ndr.Domain;
using Courier.SharedKernel;
using Courier.Shipments.Domain;

namespace Courier.Ndr.Tests;

public class NdrReportingLookupServiceTests
{
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId { get; set; }
    }

    private static readonly Guid ProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid DistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid LocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private static readonly Guid ServiceTypeId = Courier.Pricing.Infrastructure.PricingIds.ServiceTypeId(Courier.Pricing.Domain.ServiceTypeCode.Standard);

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

    private static Shipment BuildShipment(Guid orgId, Guid branchId, Guid merchantId) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = orgId, Awb = $"AWB{Guid.NewGuid():N}"[..12], MerchantId = merchantId,
        OriginBranchId = branchId, DestinationBranchId = branchId, CurrentStatus = ShipmentStatus.NdrCreated,
        ServiceTypeId = ServiceTypeId, WeightKg = 1m, DeclaredValue = 100m, CodAmount = 500m, WantsInsurance = false,
        DeliveryCharge = 100m, InsuranceCharge = 0m, OtherCharges = 0m, TotalCharge = 100m,
        RecipientName = "R", RecipientPhone = "9800000000", RecipientProvinceId = ProvinceId, RecipientDistrictId = DistrictId,
        RecipientLocalLevelId = LocalLevelId, RecipientWardNumber = 1, RecipientAddressLine = "Addr",
        SenderName = "S", SenderPhone = "9800000001", SenderProvinceId = ProvinceId, SenderDistrictId = DistrictId,
        SenderLocalLevelId = LocalLevelId, SenderWardNumber = 1, SenderAddressLine = "Addr"
    };

    [Fact]
    public async Task GetOpenNdrCountForBranchAsync_CountsOnlyUnresolvedNdrsAtThatBranch()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var shipment = BuildShipment(orgId, branchId, merchantId);
        dbContext.Add(shipment);
        dbContext.Add(new NDR { Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = shipment.Id, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open });
        var resolvedShipment = BuildShipment(orgId, branchId, merchantId);
        dbContext.Add(resolvedShipment);
        dbContext.Add(new NDR { Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = resolvedShipment.Id, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Resolved, Resolution = NdrResolution.Reattempt, ResolvedAt = DateTimeOffset.UtcNow });
        await dbContext.SaveChangesAsync();

        var service = new NdrReportingLookupService(dbContext);
        var count = await service.GetOpenNdrCountForBranchAsync(branchId);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetShipmentIdsWithAnyNdrAsync_ReturnsOnlyIdsWithAnNdrRow()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var withNdr = BuildShipment(orgId, branchId, merchantId);
        var withoutNdr = BuildShipment(orgId, branchId, merchantId);
        dbContext.Add(withNdr);
        dbContext.Add(withoutNdr);
        dbContext.Add(new NDR { Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = withNdr.Id, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open });
        await dbContext.SaveChangesAsync();

        var service = new NdrReportingLookupService(dbContext);
        var result = await service.GetShipmentIdsWithAnyNdrAsync(new HashSet<Guid> { withNdr.Id, withoutNdr.Id });

        Assert.Contains(withNdr.Id, result);
        Assert.DoesNotContain(withoutNdr.Id, result);
    }

    [Fact]
    public async Task GetShipmentIdsWithRtoAsync_ReturnsOnlyOriginalShipmentIdsWithAReturnShipment()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var withRto = BuildShipment(orgId, branchId, merchantId);
        dbContext.Add(withRto);
        dbContext.Add(new ReturnShipment { Id = Guid.NewGuid(), OrganizationId = orgId, OriginalShipmentId = withRto.Id, ReturnAwb = "RTN000001", RtoReason = "CustomerRefused", OriginBranchId = branchId, DestinationBranchId = branchId });
        await dbContext.SaveChangesAsync();

        var service = new NdrReportingLookupService(dbContext);
        var result = await service.GetShipmentIdsWithRtoAsync(new HashSet<Guid> { withRto.Id });

        Assert.Contains(withRto.Id, result);
    }

    [Fact]
    public async Task GetShipmentIdsWithAnyNdrAsync_EmptyInput_ReturnsEmptySet()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var service = new NdrReportingLookupService(dbContext);

        var result = await service.GetShipmentIdsWithAnyNdrAsync(new HashSet<Guid>());

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetStatusCountsAsync_GroupsByStatus_ScopedByShipmentDestinationBranch()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        // A second, real branch for the out-of-scope shipment's DestinationBranchId -- Shipment
        // has a real foreign key to Branches, so a random unseeded Guid would violate it.
        var otherBranchId = Guid.NewGuid();
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = otherBranchId, OrganizationId = orgId, Code = "BR2", Name = "Branch Two",
            Type = Courier.Branches.Domain.BranchType.Branch, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });

        var inScopeShipment = BuildShipment(orgId, branchId, merchantId);
        var outOfScopeShipment = BuildShipment(orgId, branchId, merchantId);
        outOfScopeShipment.DestinationBranchId = otherBranchId;
        dbContext.Add(inScopeShipment);
        dbContext.Add(outOfScopeShipment);
        dbContext.Add(new NDR { Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = inScopeShipment.Id, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open });
        dbContext.Add(new NDR { Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = outOfScopeShipment.Id, Reason = NdrReason.WrongAddress, Status = NdrStatus.Contacting });
        await dbContext.SaveChangesAsync();

        var service = new NdrReportingLookupService(dbContext);
        var result = await service.GetStatusCountsAsync(new HashSet<Guid> { branchId });

        Assert.Equal(1, result[NdrStatus.Open]);
        Assert.False(result.ContainsKey(NdrStatus.Contacting));
    }

    [Fact]
    public async Task GetOpenNdrCountsByBranchAsync_CountsUnresolvedNdrsByDestinationBranch()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        var otherBranchId = Guid.NewGuid();
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = otherBranchId, OrganizationId = orgId, Code = "BR2", Name = "Branch Two",
            Type = Courier.Branches.Domain.BranchType.Branch, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });
        await dbContext.SaveChangesAsync();
        tenantProvider.OrganizationId = orgId;

        void AddNdr(Guid destinationBranchId, NdrStatus status)
        {
            var shipment = BuildShipment(orgId, destinationBranchId, merchantId);
            dbContext.Add(shipment);
            dbContext.Add(new NDR
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = shipment.Id, Reason = NdrReason.CustomerUnavailable, Status = status,
                Resolution = status == NdrStatus.Resolved ? NdrResolution.Reattempt : null,
                ResolvedAt = status == NdrStatus.Resolved ? DateTimeOffset.UtcNow : null
            });
        }
        AddNdr(branchId, NdrStatus.Open);
        AddNdr(branchId, NdrStatus.Contacting);
        AddNdr(branchId, NdrStatus.Resolved);
        AddNdr(otherBranchId, NdrStatus.RtoRequested);
        await dbContext.SaveChangesAsync();
        var service = new NdrReportingLookupService(dbContext);

        var all = await service.GetOpenNdrCountsByBranchAsync(null);
        var filtered = await service.GetOpenNdrCountsByBranchAsync(new HashSet<Guid> { otherBranchId });

        Assert.Equal(2, all[branchId]);
        Assert.Equal(1, all[otherBranchId]);
        Assert.Equal(otherBranchId, Assert.Single(filtered).Key);
    }
}
