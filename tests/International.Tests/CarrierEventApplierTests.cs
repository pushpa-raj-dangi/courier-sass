using Courier.International.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Tests;

public class CarrierEventApplierTests
{
    private sealed record Harness(CarrierTestDbContext DbContext, Guid OrganizationId, CarrierAccount Account, RecordingGateway Gateway, CarrierEventApplier Applier);

    private static async Task<Harness> CreateHarnessAsync()
    {
        var dbContext = CarrierTestData.NewContext();
        var organizationId = Guid.NewGuid();
        var account = await CarrierTestData.SeedAccountAsync(dbContext, organizationId, "TESTCARRIER");
        var gateway = new RecordingGateway();
        var registry = new CarrierProviderRegistry(new ICarrierProvider[] { new TestCarrierProvider() });
        return new Harness(dbContext, organizationId, account, gateway, new CarrierEventApplier(dbContext, registry, gateway));
    }

    private static async Task<(CarrierBooking Booking, Guid ShipmentId)> SeedBookedAsync(Harness h, string awb,
        CarrierBookingDirection direction = CarrierBookingDirection.Outbound, CarrierBookingStatus status = CarrierBookingStatus.Booked, Guid? accountId = null)
    {
        var shipmentId = Guid.NewGuid();
        var leg = await CarrierTestData.SeedLegAsync(h.DbContext, h.OrganizationId, shipmentId);
        var booking = await CarrierTestData.SeedBookingAsync(h.DbContext, h.OrganizationId, leg.Id, accountId ?? h.Account.Id, status, awb, direction);
        return (booking, shipmentId);
    }

    private static async Task<CarrierTrackingEvent> ReloadAsync(Harness h, Guid eventId)
    {
        h.DbContext.ChangeTracker.Clear();
        return await h.DbContext.Set<CarrierTrackingEvent>().SingleAsync(e => e.Id == eventId);
    }

    [Fact]
    public async Task MatchedMappedEvent_IsAppliedThroughTheGateway()
    {
        var h = await CreateHarnessAsync();
        var (booking, shipmentId) = await SeedBookedAsync(h, "AWB-1");
        var occurredAt = DateTimeOffset.UtcNow.AddHours(-2);
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "AWB-1", "TRANSIT", occurredAt);

        await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

        var call = Assert.Single(h.Gateway.CarrierApplies);
        Assert.Equal(shipmentId, call.ShipmentId);
        Assert.Equal(ShipmentStatus.InInternationalTransit, call.Target);
        Assert.Equal(occurredAt, call.OccurredAt);
        Assert.Equal(evt.CarrierEventId, call.Reference.CarrierEventId);
        var stored = await ReloadAsync(h, evt.Id);
        Assert.Equal(CarrierEventProcessingStatus.Applied, stored.ProcessingStatus);
        Assert.Equal(booking.Id, stored.CarrierBookingId);
        Assert.Equal("InInternationalTransit", stored.MappedShipmentStatus);
        Assert.NotNull(stored.ProcessedAt);
        Assert.Null(stored.ClaimedAt);
    }

    [Fact]
    public async Task Applier_MatchesBookingByAccountAndAwb_NotByAwbAlone()
    {
        var h = await CreateHarnessAsync();
        var otherAccount = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        await SeedBookedAsync(h, "SAME-AWB", accountId: otherAccount.Id);
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "SAME-AWB", "TRANSIT", DateTimeOffset.UtcNow);

        await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

        Assert.Empty(h.Gateway.CarrierApplies);
        Assert.Equal(CarrierEventProcessingStatus.Unmatched, (await ReloadAsync(h, evt.Id)).ProcessingStatus);
    }

    [Fact]
    public async Task Applier_EventForCancelledBooking_IsUnmatched()
    {
        var h = await CreateHarnessAsync();
        await SeedBookedAsync(h, "AWB-1", status: CarrierBookingStatus.Cancelled);
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "AWB-1", "TRANSIT", DateTimeOffset.UtcNow);

        await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

        Assert.Empty(h.Gateway.CarrierApplies);
        Assert.Equal(CarrierEventProcessingStatus.Unmatched, (await ReloadAsync(h, evt.Id)).ProcessingStatus);
    }

    [Fact]
    public async Task InformationalCode_IsIgnoredWithoutCallingTheGateway()
    {
        var h = await CreateHarnessAsync();
        await SeedBookedAsync(h, "AWB-1");
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "AWB-1", "INFO", DateTimeOffset.UtcNow);

        await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

        Assert.Empty(h.Gateway.CarrierApplies);
        Assert.Equal(CarrierEventProcessingStatus.Ignored, (await ReloadAsync(h, evt.Id)).ProcessingStatus);
    }

    [Fact]
    public async Task InboundBooking_MapsWithInboundDirection()
    {
        var h = await CreateHarnessAsync();
        await SeedBookedAsync(h, "AWB-IN", CarrierBookingDirection.Inbound);
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "AWB-IN", "CLEARANCE", DateTimeOffset.UtcNow);

        await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

        Assert.Equal(ShipmentStatus.CustomsClearance, Assert.Single(h.Gateway.CarrierApplies).Target);
    }

    [Fact]
    public async Task AlreadyAtStatus_IsIgnored()
    {
        var h = await CreateHarnessAsync();
        await SeedBookedAsync(h, "AWB-1");
        h.Gateway.ApplyResult = CarrierStatusApplyResult.AlreadyAtStatus;
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "AWB-1", "TRANSIT", DateTimeOffset.UtcNow);

        await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

        Assert.Equal(CarrierEventProcessingStatus.Ignored, (await ReloadAsync(h, evt.Id)).ProcessingStatus);
    }

    [Fact]
    public async Task InvalidTransition_IsRejectedWithTheReason()
    {
        var h = await CreateHarnessAsync();
        await SeedBookedAsync(h, "AWB-1");
        h.Gateway.ApplyException = new ShipmentInvalidTransitionException(Guid.NewGuid(), ShipmentStatus.CustomsPrepared, ShipmentStatus.InInternationalTransit);
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "AWB-1", "TRANSIT", DateTimeOffset.UtcNow);

        await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

        var stored = await ReloadAsync(h, evt.Id);
        Assert.Equal(CarrierEventProcessingStatus.Rejected, stored.ProcessingStatus);
        Assert.Contains("Cannot transition", stored.ProcessingNote);
        Assert.Equal("InInternationalTransit", stored.MappedShipmentStatus);
    }

    [Fact]
    public async Task UnexpectedFailure_ReturnsToPending_UntilTheAttemptCap()
    {
        var h = await CreateHarnessAsync();
        await SeedBookedAsync(h, "AWB-1");
        h.Gateway.ApplyException = new InvalidOperationException("database hiccup");
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "AWB-1", "TRANSIT", DateTimeOffset.UtcNow);

        for (var attempt = 1; attempt <= CarrierEventApplier.MaxAttempts; attempt++)
        {
            var current = await ReloadAsync(h, evt.Id);
            current.ProcessingStatus = CarrierEventProcessingStatus.Processing;
            await h.DbContext.SaveChangesAsync();

            await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

            var stored = await ReloadAsync(h, evt.Id);
            Assert.Equal(attempt, stored.ProcessingAttempts);
            Assert.Equal(attempt < CarrierEventApplier.MaxAttempts ? CarrierEventProcessingStatus.Pending : CarrierEventProcessingStatus.Rejected, stored.ProcessingStatus);
        }
    }

    [Fact]
    public async Task EventNotClaimed_IsLeftAlone()
    {
        var h = await CreateHarnessAsync();
        await SeedBookedAsync(h, "AWB-1");
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "AWB-1", "TRANSIT", DateTimeOffset.UtcNow, CarrierEventProcessingStatus.Pending);

        await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

        Assert.Empty(h.Gateway.CarrierApplies);
        Assert.Equal(CarrierEventProcessingStatus.Pending, (await ReloadAsync(h, evt.Id)).ProcessingStatus);
    }
}
