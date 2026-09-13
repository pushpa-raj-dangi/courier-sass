using Courier.Geography.Infrastructure;
using Courier.Persistence;
using Courier.Pricing.Domain;
using Courier.Pricing.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Tests;

public class ShipmentReportingLookupServiceTests
{
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId { get; set; }
    }

    private static readonly Guid ProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid DistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid LocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private static readonly Guid ServiceTypeId = PricingIds.ServiceTypeId(ServiceTypeCode.Standard);

    private static async Task<(Guid OrgId, Guid OriginBranchId, Guid DestBranchId, Guid MerchantId)> SeedOrgAsync(AppDbContext dbContext)
    {
        var orgId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var destBranchId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        dbContext.Add(new Courier.Identity.Domain.Organization { Id = orgId, Name = $"Org {orgId}", Slug = $"org-{orgId:N}" });
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = originBranchId, OrganizationId = orgId, Code = "ORG", Name = "Origin Branch",
            Type = Courier.Branches.Domain.BranchType.Hub, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = destBranchId, OrganizationId = orgId, Code = "DST", Name = "Destination Branch",
            Type = Courier.Branches.Domain.BranchType.Branch, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });
        dbContext.Add(new Courier.Merchants.Domain.Merchant
        {
            Id = merchantId, OrganizationId = orgId, LegalName = "Test Merchant Pvt Ltd", DisplayName = "Test Merchant"
        });
        await dbContext.SaveChangesAsync();
        return (orgId, originBranchId, destBranchId, merchantId);
    }

    private static Shipment BuildShipment(Guid orgId, Guid originBranchId, Guid destBranchId, Guid merchantId, ShipmentStatus status, Guid? currentBranchId = null) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = orgId,
        Awb = $"AWB{Guid.NewGuid():N}"[..12],
        MerchantId = merchantId,
        OriginBranchId = originBranchId,
        DestinationBranchId = destBranchId,
        CurrentBranchId = currentBranchId,
        CurrentStatus = status,
        ServiceTypeId = ServiceTypeId,
        WeightKg = 1m,
        DeclaredValue = 100m,
        CodAmount = 500m,
        WantsInsurance = false,
        DeliveryCharge = 100m,
        InsuranceCharge = 0m,
        OtherCharges = 0m,
        TotalCharge = 100m,
        RecipientName = "Recipient", RecipientPhone = "9800000000",
        RecipientProvinceId = ProvinceId, RecipientDistrictId = DistrictId, RecipientLocalLevelId = LocalLevelId,
        RecipientWardNumber = 1, RecipientAddressLine = "Recipient Address",
        SenderName = "Sender", SenderPhone = "9800000001",
        SenderProvinceId = ProvinceId, SenderDistrictId = DistrictId, SenderLocalLevelId = LocalLevelId,
        SenderWardNumber = 1, SenderAddressLine = "Sender Address"
    };

    [Fact]
    public async Task GetCohortStatusCountsAsync_CountsOnlyShipmentsCreatedInRange_ByCurrentStatus()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var inRange = BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Delivered);
        var outOfRange = BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Delivered);
        dbContext.Add(inRange);
        dbContext.Add(outOfRange);

        var now = DateTimeOffset.UtcNow;
        dbContext.Add(new ShipmentEvent { Id = Guid.NewGuid(), ShipmentId = inRange.Id, EventType = ShipmentEventType.Created, NewStatus = ShipmentStatus.Created, Timestamp = now });
        dbContext.Add(new ShipmentEvent { Id = Guid.NewGuid(), ShipmentId = outOfRange.Id, EventType = ShipmentEventType.Created, NewStatus = ShipmentStatus.Created, Timestamp = now.AddDays(-5) });
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var result = await service.GetCohortStatusCountsAsync(null, now.AddHours(-1), now.AddHours(1));

        Assert.Equal(1, result[ShipmentStatus.Delivered]);
    }

    [Fact]
    public async Task GetCohortStatusCountsAsync_ExcludesAnotherOrganizationsShipments()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgAId, originA, destA, merchantA) = await SeedOrgAsync(dbContext);
        var (orgBId, originB, destB, merchantB) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgAId;

        var shipmentA = BuildShipment(orgAId, originA, destA, merchantA, ShipmentStatus.Created);
        var shipmentB = BuildShipment(orgBId, originB, destB, merchantB, ShipmentStatus.Created);
        dbContext.Add(shipmentA);
        dbContext.Add(shipmentB);

        var now = DateTimeOffset.UtcNow;
        dbContext.Add(new ShipmentEvent { Id = Guid.NewGuid(), ShipmentId = shipmentA.Id, EventType = ShipmentEventType.Created, NewStatus = ShipmentStatus.Created, Timestamp = now });
        dbContext.Add(new ShipmentEvent { Id = Guid.NewGuid(), ShipmentId = shipmentB.Id, EventType = ShipmentEventType.Created, NewStatus = ShipmentStatus.Created, Timestamp = now });
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var result = await service.GetCohortStatusCountsAsync(null, now.AddHours(-1), now.AddHours(1));

        Assert.Equal(1, result[ShipmentStatus.Created]);
    }

    [Fact]
    public async Task GetBranchShipmentCountsAsync_BucketsByCurrentStatusAtBranch()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        dbContext.Add(BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.AtOriginBranch, originBranchId));
        dbContext.Add(BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Sorted, originBranchId));
        dbContext.Add(BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Sorted, originBranchId));
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var result = await service.GetBranchShipmentCountsAsync(originBranchId);

        Assert.Equal(1, result.PendingSorting);
        Assert.Equal(2, result.PendingDispatch);
        Assert.Equal(3, result.Outgoing);
    }

    [Fact]
    public async Task GetAverageDeliveryTimeAsync_AveragesCreatedToDeliveredDuration()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var shipment = BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Delivered);
        dbContext.Add(shipment);
        var created = DateTimeOffset.UtcNow.AddHours(-3);
        var delivered = created.AddHours(2);
        dbContext.Add(new ShipmentEvent { Id = Guid.NewGuid(), ShipmentId = shipment.Id, EventType = ShipmentEventType.Created, NewStatus = ShipmentStatus.Created, Timestamp = created });
        dbContext.Add(new ShipmentEvent { Id = Guid.NewGuid(), ShipmentId = shipment.Id, EventType = ShipmentEventType.Delivered, NewStatus = ShipmentStatus.Delivered, Timestamp = delivered });
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var result = await service.GetAverageDeliveryTimeAsync(null, created.AddHours(-1), delivered.AddHours(1));

        Assert.NotNull(result);
        Assert.True(Math.Abs((result!.Value - TimeSpan.FromHours(2)).TotalSeconds) < 1,
            $"Expected ~2 hours, got {result.Value}");
    }

    [Fact]
    public async Task GetShipmentIdsByDestinationBranchAsync_BucketsByDestinationBranch_RespectsBranchFilter()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var shipment = BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.InTransit);
        dbContext.Add(shipment);
        var now = DateTimeOffset.UtcNow;
        dbContext.Add(new ShipmentEvent { Id = Guid.NewGuid(), ShipmentId = shipment.Id, EventType = ShipmentEventType.Created, NewStatus = ShipmentStatus.Created, Timestamp = now });
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);

        var allBranches = await service.GetShipmentIdsByDestinationBranchAsync(null, now.AddHours(-1), now.AddHours(1));
        Assert.Contains(shipment.Id, allBranches[destBranchId]);

        var restricted = await service.GetShipmentIdsByDestinationBranchAsync(new HashSet<Guid> { originBranchId }, now.AddHours(-1), now.AddHours(1));
        Assert.False(restricted.ContainsKey(destBranchId));
    }

    [Fact]
    public async Task GetShipmentExportRowsAsync_ReturnsRowWithDeliveredAtNullWhenNotYetDelivered()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var shipment = BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.InTransit);
        dbContext.Add(shipment);
        var now = DateTimeOffset.UtcNow;
        dbContext.Add(new ShipmentEvent { Id = Guid.NewGuid(), ShipmentId = shipment.Id, EventType = ShipmentEventType.Created, NewStatus = ShipmentStatus.Created, Timestamp = now });
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var rows = await service.GetShipmentExportRowsAsync(null, now.AddHours(-1), now.AddHours(1));

        var row = Assert.Single(rows, r => r.Awb == shipment.Awb);
        Assert.Null(row.DeliveredAt);
        Assert.Equal(shipment.MerchantId, row.MerchantId);
    }

    [Fact]
    public async Task GetAwbsByShipmentIdsAsync_ReturnsAwbPerId()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var shipment = BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Created);
        dbContext.Add(shipment);
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var result = await service.GetAwbsByShipmentIdsAsync(new HashSet<Guid> { shipment.Id });

        Assert.Equal(shipment.Awb, result[shipment.Id]);
    }

    [Fact]
    public async Task GetCurrentStatusCountsAsync_GroupsByCurrentStatus_NoBranchFilter()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        dbContext.Add(BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Delivered));
        dbContext.Add(BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Delivered));
        dbContext.Add(BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Created));
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var result = await service.GetCurrentStatusCountsAsync(null);

        Assert.Equal(2, result[ShipmentStatus.Delivered]);
        Assert.Equal(1, result[ShipmentStatus.Created]);
    }

    [Fact]
    public async Task GetCurrentStatusCountsAsync_BranchRestricted_IncludesShipmentWithNullCurrentBranchIdViaOriginOrDestination()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        // OutForDelivery shipment has CurrentBranchId == null (with a rider) -- must still count
        // toward its origin branch's restricted total via the OriginBranchId arm of the filter,
        // not be silently dropped the way a bare CurrentBranchId-only filter would drop it.
        var outForDelivery = BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.OutForDelivery, currentBranchId: null);
        dbContext.Add(outForDelivery);
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var result = await service.GetCurrentStatusCountsAsync(new HashSet<Guid> { originBranchId });

        Assert.Equal(1, result[ShipmentStatus.OutForDelivery]);
    }

    [Fact]
    public async Task GetCurrentStatusCountsAsync_BranchRestricted_ExcludesShipmentTouchingNoAccessibleBranch()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        dbContext.Add(BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.InTransit));
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var result = await service.GetCurrentStatusCountsAsync(new HashSet<Guid> { Guid.NewGuid() });

        Assert.False(result.ContainsKey(ShipmentStatus.InTransit));
    }
}
