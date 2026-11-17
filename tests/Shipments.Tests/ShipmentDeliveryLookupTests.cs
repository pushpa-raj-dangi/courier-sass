using Courier.Cod.Application;
using Courier.Geography.Infrastructure;
using Courier.Persistence;
using Courier.Pricing.Domain;
using Courier.Pricing.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;

namespace Courier.Shipments.Tests;

public class ShipmentDeliveryLookupTests
{
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId { get; set; }
    }

    private static readonly Guid ProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid DistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid LocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private static readonly Guid ServiceTypeId = PricingIds.ServiceTypeId(ServiceTypeCode.Standard);

    private static Shipment Build(Guid orgId, Guid origin, Guid dest, Guid merchantId, ShipmentStatus status) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = orgId, Awb = $"AWB{Guid.NewGuid():N}"[..12], MerchantId = merchantId,
        OriginBranchId = origin, DestinationBranchId = dest, CurrentStatus = status, ServiceTypeId = ServiceTypeId,
        WeightKg = 1m, DeclaredValue = 100m, CodAmount = 500m, DeliveryCharge = 100m, TotalCharge = 100m,
        RecipientName = "Recipient", RecipientPhone = "9800000000",
        RecipientProvinceId = ProvinceId, RecipientDistrictId = DistrictId, RecipientLocalLevelId = LocalLevelId,
        RecipientWardNumber = 1, RecipientAddressLine = "Recipient Address",
        SenderName = "Sender", SenderPhone = "9800000001",
        SenderProvinceId = ProvinceId, SenderDistrictId = DistrictId, SenderLocalLevelId = LocalLevelId,
        SenderWardNumber = 1, SenderAddressLine = "Sender Address"
    };

    private static ShipmentEvent Evt(Guid shipmentId, ShipmentEventType type, DateTimeOffset ts) => new()
    {
        Id = Guid.NewGuid(), ShipmentId = shipmentId, EventType = type, NewStatus = ShipmentStatus.Delivered, Timestamp = ts
    };

    [Fact]
    public async Task CountDeliveredAsync_CountsDeliveredAndDeliveredAbroadInRange_PerMerchant()
    {
        var tenant = new FixedTenantProvider();
        await using var db = TestDbContextFactory.Create(tenant);
        var orgId = Guid.NewGuid();
        var originId = Guid.NewGuid();
        var destId = Guid.NewGuid();
        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        db.Add(new Courier.Identity.Domain.Organization { Id = orgId, Name = $"Org {orgId}", Slug = $"org-{orgId:N}" });
        foreach (var (id, code) in new[] { (originId, "ORG"), (destId, "DST") })
        {
            db.Add(new Courier.Branches.Domain.Branch
            {
                Id = id, OrganizationId = orgId, Code = code, Name = code, Type = Courier.Branches.Domain.BranchType.Hub,
                ProvinceId = ProvinceId, DistrictId = DistrictId, LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Addr"
            });
        }
        foreach (var (id, name) in new[] { (merchantA, "A"), (merchantB, "B") })
        {
            db.Add(new Courier.Merchants.Domain.Merchant { Id = id, OrganizationId = orgId, LegalName = $"Merchant {name}", DisplayName = $"Merchant {name}" });
        }
        await db.SaveChangesAsync();
        tenant.OrganizationId = orgId;

        var from = DateTimeOffset.UtcNow.AddHours(-2);
        var to = DateTimeOffset.UtcNow.AddHours(2);
        var a1 = Build(orgId, originId, destId, merchantA, ShipmentStatus.Delivered);
        var a2 = Build(orgId, originId, destId, merchantA, ShipmentStatus.Delivered);
        var aOld = Build(orgId, originId, destId, merchantA, ShipmentStatus.Delivered);
        var aAbroad = Build(orgId, originId, destId, merchantA, ShipmentStatus.Delivered);
        var b1 = Build(orgId, originId, destId, merchantB, ShipmentStatus.Delivered);
        db.AddRange(a1, a2, aOld, aAbroad, b1);
        var mid = from.AddHours(1);
        db.AddRange(
            Evt(a1.Id, ShipmentEventType.Delivered, mid),
            Evt(a2.Id, ShipmentEventType.Delivered, mid),
            Evt(aOld.Id, ShipmentEventType.Delivered, from.AddHours(-1)),
            Evt(aAbroad.Id, ShipmentEventType.DeliveredAbroad, mid),
            Evt(b1.Id, ShipmentEventType.Delivered, mid));
        await db.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(db);
        Assert.Equal(3, await service.CountDeliveredAsync(merchantA, from, to));
        Assert.Equal(1, await service.CountDeliveredAsync(merchantB, from, to));

        Assert.True(await service.BelongsToMerchantAsync(a1.Id, merchantA));
        Assert.False(await service.BelongsToMerchantAsync(a1.Id, merchantB));
        Assert.False(await service.BelongsToMerchantAsync(Guid.NewGuid(), merchantA));
    }

    [Fact]
    public async Task Lookups_AreTenantScoped_ShipmentsOfAnotherOrganizationAreInvisible()
    {
        var tenant = new FixedTenantProvider();
        await using var db = TestDbContextFactory.Create(tenant);
        var orgOne = Guid.NewGuid();
        var orgTwo = Guid.NewGuid();
        var merchantOne = Guid.NewGuid();
        var merchantTwo = Guid.NewGuid();
        var branchesOne = (Guid.NewGuid(), Guid.NewGuid());
        var branchesTwo = (Guid.NewGuid(), Guid.NewGuid());
        foreach (var (org, merchant, branches) in new[] { (orgOne, merchantOne, branchesOne), (orgTwo, merchantTwo, branchesTwo) })
        {
            db.Add(new Courier.Identity.Domain.Organization { Id = org, Name = $"Org {org}", Slug = $"org-{org:N}" });
            foreach (var (id, code) in new[] { (branches.Item1, "ORG"), (branches.Item2, "DST") })
            {
                db.Add(new Courier.Branches.Domain.Branch
                {
                    Id = id, OrganizationId = org, Code = code, Name = code, Type = Courier.Branches.Domain.BranchType.Hub,
                    ProvinceId = ProvinceId, DistrictId = DistrictId, LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Addr"
                });
            }
            db.Add(new Courier.Merchants.Domain.Merchant { Id = merchant, OrganizationId = org, LegalName = $"M {merchant}", DisplayName = $"M {merchant}" });
        }
        await db.SaveChangesAsync();

        var from = DateTimeOffset.Parse("2026-10-05T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-10-06T00:00:00Z");
        var mid = DateTimeOffset.Parse("2026-10-05T12:00:00Z");
        var own = Build(orgOne, branchesOne.Item1, branchesOne.Item2, merchantOne, ShipmentStatus.Delivered);
        var foreign = Build(orgTwo, branchesTwo.Item1, branchesTwo.Item2, merchantTwo, ShipmentStatus.Delivered);
        // A shipment that sits in org two but names org one's merchant: tenant filtering alone must hide it.
        var foreignSameMerchantValue = Build(orgTwo, branchesTwo.Item1, branchesTwo.Item2, merchantOne, ShipmentStatus.Delivered);
        db.AddRange(own, foreign, foreignSameMerchantValue);
        db.AddRange(
            Evt(own.Id, ShipmentEventType.Delivered, mid),
            Evt(foreign.Id, ShipmentEventType.Delivered, mid),
            Evt(foreignSameMerchantValue.Id, ShipmentEventType.Delivered, mid));
        await db.SaveChangesAsync();

        tenant.OrganizationId = orgOne;
        var service = new ShipmentReportingLookupService(db);

        Assert.Equal(1, await service.CountDeliveredAsync(merchantOne, from, to));
        Assert.Equal(0, await service.CountDeliveredAsync(merchantTwo, from, to));
        Assert.True(await service.BelongsToMerchantAsync(own.Id, merchantOne));
        Assert.False(await service.BelongsToMerchantAsync(foreign.Id, merchantTwo));
        Assert.False(await service.BelongsToMerchantAsync(foreignSameMerchantValue.Id, merchantOne));
    }
}
