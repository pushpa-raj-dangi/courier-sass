using Courier.International.Application;
using Courier.International.Domain;
using Courier.Persistence;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests.International;

public class CarrierEventProcessingTests : InternationalApiTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public CarrierEventProcessingTests(WebApplicationFactory<Program> factory) : base(factory) { }

    private async Task<(Guid OrganizationId, Guid ShipmentId, Guid AccountId)> SeedHandedOverAsync(string label, string carrierAwb)
    {
        var org = await SetUpOrgAsync(label);
        var created = await CreateOutboundAsync(org);
        await SetShipmentStateAsync(created.Shipment.Id, ShipmentStatus.HandedToCarrier, null);
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var legId = await dbContext.Set<InternationalLeg>().IgnoreQueryFilters().Where(l => l.ShipmentId == created.Shipment.Id).Select(l => l.Id).SingleAsync();
        var account = new CarrierAccount
        {
            Id = Guid.NewGuid(), OrganizationId = org.Auth.OrganizationId, CarrierCode = "MANUAL", DisplayName = "Manual",
            WebhookPublicId = Guid.NewGuid().ToString("N"), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Set<CarrierAccount>().Add(account);
        dbContext.Set<CarrierBooking>().Add(new CarrierBooking
        {
            Id = Guid.NewGuid(), OrganizationId = org.Auth.OrganizationId, InternationalLegId = legId, CarrierAccountId = account.Id,
            Direction = CarrierBookingDirection.Outbound, Status = CarrierBookingStatus.Booked, CarrierAwb = carrierAwb,
            IdempotencyKey = Guid.NewGuid().ToString("N"), BookedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        return (org.Auth.OrganizationId, created.Shipment.Id, account.Id);
    }

    private async Task<Guid> InsertPendingEventAsync(Guid organizationId, Guid accountId, string carrierAwb, string code, DateTimeOffset occurredAt)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evt = new CarrierTrackingEvent
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, CarrierAccountId = accountId, CarrierAwb = carrierAwb,
            CarrierEventId = $"evt-{Guid.NewGuid():N}", CarrierStatusCode = code, OccurredAt = occurredAt,
            Source = CarrierEventSource.Manual, ReceivedAt = DateTimeOffset.UtcNow, ProcessingStatus = CarrierEventProcessingStatus.Pending
        };
        dbContext.Set<CarrierTrackingEvent>().Add(evt);
        await dbContext.SaveChangesAsync();
        return evt.Id;
    }

    private async Task<CarrierTrackingEvent> EventAsync(Guid eventId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<CarrierTrackingEvent>().IgnoreQueryFilters().SingleAsync(e => e.Id == eventId);
    }

    [Fact]
    public async Task Processor_AppliesEventsInOccurredAtOrder_EvenWhenInsertedOutOfOrder()
    {
        var (organizationId, shipmentId, accountId) = await SeedHandedOverAsync("order", "ORD-AWB");
        var now = DateTimeOffset.UtcNow;
        var delivered = await InsertPendingEventAsync(organizationId, accountId, "ORD-AWB", "DELIVERED", now.AddMinutes(-1));
        var transit = await InsertPendingEventAsync(organizationId, accountId, "ORD-AWB", "IN_TRANSIT", now.AddHours(-3));

        await ProcessCarrierEventsAsync(accountId);

        Assert.Equal(ShipmentStatus.DeliveredAbroad, await ShipmentStatusAsync(shipmentId));
        Assert.Equal(CarrierEventProcessingStatus.Applied, (await EventAsync(transit)).ProcessingStatus);
        Assert.Equal(CarrierEventProcessingStatus.Applied, (await EventAsync(delivered)).ProcessingStatus);
    }

    [Fact]
    public async Task Processor_AppliesEachTenantsEventsToItsOwnShipment()
    {
        var a = await SeedHandedOverAsync("tenant-a", "SAME-AWB-1");
        var b = await SeedHandedOverAsync("tenant-b", "SAME-AWB-1");
        var eventA = await InsertPendingEventAsync(a.OrganizationId, a.AccountId, "SAME-AWB-1", "IN_TRANSIT", DateTimeOffset.UtcNow);

        await ProcessCarrierEventsAsync(a.AccountId);

        Assert.Equal(ShipmentStatus.InInternationalTransit, await ShipmentStatusAsync(a.ShipmentId));
        Assert.Equal(ShipmentStatus.HandedToCarrier, await ShipmentStatusAsync(b.ShipmentId));

        var eventB = await InsertPendingEventAsync(b.OrganizationId, b.AccountId, "SAME-AWB-1", "CUSTOMS_HOLD", DateTimeOffset.UtcNow);
        await ProcessCarrierEventsAsync(b.AccountId);
        Assert.Equal(CarrierEventProcessingStatus.Rejected, (await EventAsync(eventB)).ProcessingStatus); // HandedToCarrier -> CustomsHold is not allowed
        Assert.Equal(ShipmentStatus.InInternationalTransit, await ShipmentStatusAsync(a.ShipmentId));
    }

    [Fact]
    public async Task Processor_InvalidTransition_IsRejectedAndShipmentUnchanged()
    {
        var (organizationId, shipmentId, accountId) = await SeedHandedOverAsync("invalid", "INV-AWB");
        var evt = await InsertPendingEventAsync(organizationId, accountId, "INV-AWB", "DELIVERED", DateTimeOffset.UtcNow);

        await ProcessCarrierEventsAsync(accountId);

        var stored = await EventAsync(evt);
        Assert.Equal(CarrierEventProcessingStatus.Rejected, stored.ProcessingStatus);
        Assert.Contains("Cannot transition", stored.ProcessingNote);
        Assert.Equal(ShipmentStatus.HandedToCarrier, await ShipmentStatusAsync(shipmentId));
    }

    [Fact]
    public async Task Processor_AppliedTransition_WritesACarrierShipmentEvent()
    {
        var (organizationId, shipmentId, accountId) = await SeedHandedOverAsync("event", "EVT-AWB");
        var occurredAt = DateTimeOffset.UtcNow.AddHours(-1);
        var evt = await InsertPendingEventAsync(organizationId, accountId, "EVT-AWB", "IN_TRANSIT", occurredAt);

        await ProcessCarrierEventsAsync(accountId);

        using var scope = Factory.Services.CreateScope();
        var shipmentEvent = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<ShipmentEvent>()
            .SingleAsync(e => e.ShipmentId == shipmentId && e.EventType == ShipmentEventType.InInternationalTransit);
        Assert.Null(shipmentEvent.UserId);
        Assert.Equal(occurredAt.ToUnixTimeMilliseconds(), shipmentEvent.Timestamp.ToUnixTimeMilliseconds());
    }
}
