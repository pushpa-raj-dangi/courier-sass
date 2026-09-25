using System.Security.Claims;
using Courier.International.Application;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Tests;

public class InternationalCarrierLifecycleServiceTests
{
    private sealed class Harness
    {
        public required CarrierTestDbContext DbContext { get; init; }
        public required Guid OrganizationId { get; init; }
        public required Guid ShipmentId { get; init; }
        public required InternationalLeg Leg { get; init; }
        public required RecordingGateway Gateway { get; init; }
        public required RecordingAuditLogger Audit { get; init; }
        public required InternationalCarrierLifecycleService Service { get; init; }
    }

    private static async Task<Harness> CreateHarnessAsync(IReadOnlySet<Guid>? accessibleBranchIds = null)
    {
        var dbContext = CarrierTestData.NewContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var leg = await CarrierTestData.SeedLegAsync(dbContext, organizationId, shipmentId);
        var gateway = new RecordingGateway();
        var audit = new RecordingAuditLogger();
        var service = new InternationalCarrierLifecycleService(dbContext, new FixedTenantProvider(organizationId), audit, gateway,
            new FixedShipmentTransitionService(), new FixedBranchAuthorizationChecker(accessibleBranchIds));
        return new Harness { DbContext = dbContext, OrganizationId = organizationId, ShipmentId = shipmentId, Leg = leg, Gateway = gateway, Audit = audit, Service = service };
    }

    private static ClaimsPrincipal ActingUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    private static async Task<CarrierBooking> BookAsync(Harness h, CarrierBookingDirection direction = CarrierBookingDirection.Outbound)
    {
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "MANUAL");
        return await CarrierTestData.SeedBookingAsync(h.DbContext, h.OrganizationId, h.Leg.Id, account.Id, CarrierBookingStatus.Booked, "MAN-9", direction);
    }

    [Fact]
    public async Task HandoffAsync_WithBookedBooking_CallsGatewayAndStartsCarrierCustody()
    {
        var h = await CreateHarnessAsync();
        var booking = await BookAsync(h);

        var result = await h.Service.HandoffAsync(h.ShipmentId, ActingUser(), CancellationToken.None);

        Assert.Equal(ShipmentStatus.HandedToCarrier, result!.CurrentStatus);
        var call = Assert.Single(h.Gateway.Handoffs);
        Assert.Equal(new CarrierHandoffReference(booking.CarrierAccountId, "MAN-9"), call.Reference);
        h.DbContext.ChangeTracker.Clear();
        Assert.NotNull((await h.DbContext.Set<CarrierBooking>().SingleAsync()).CustodyStartedAt);
        Assert.Contains(h.Audit.Entries, e => e.Action == "carrier_booking.custody_started");
    }

    [Fact]
    public async Task HandoffAsync_WithoutBooking_IsRejectedBeforeTheGateway()
    {
        var h = await CreateHarnessAsync();

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() => h.Service.HandoffAsync(h.ShipmentId, ActingUser(), CancellationToken.None));
        Assert.Empty(h.Gateway.Handoffs);
    }

    [Fact]
    public async Task HandoffAsync_GatewayRejects_CustodyIsNotStarted()
    {
        var h = await CreateHarnessAsync();
        await BookAsync(h);
        h.Gateway.LifecycleException = new ShipmentInvalidTransitionException(h.ShipmentId, ShipmentStatus.Sorted, ShipmentStatus.HandedToCarrier);

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => h.Service.HandoffAsync(h.ShipmentId, ActingUser(), CancellationToken.None));
        h.DbContext.ChangeTracker.Clear();
        Assert.Null((await h.DbContext.Set<CarrierBooking>().SingleAsync()).CustodyStartedAt);
    }

    [Fact]
    public async Task ReceiveAsync_RequiresALinkedInboundBooking()
    {
        var h = await CreateHarnessAsync();

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() => h.Service.ReceiveAsync(h.ShipmentId, ActingUser(), CancellationToken.None));

        await BookAsync(h, CarrierBookingDirection.Inbound);
        var result = await h.Service.ReceiveAsync(h.ShipmentId, ActingUser(), CancellationToken.None);
        Assert.Equal(ShipmentStatus.ReceivedFromCarrier, result!.CurrentStatus);
    }

    [Fact]
    public async Task OverrideStatusAsync_PassesTargetAndReasonToTheGateway()
    {
        var h = await CreateHarnessAsync();

        await h.Service.OverrideStatusAsync(h.ShipmentId, new StatusOverrideRequest(ShipmentStatus.InInternationalTransit, "Released"), ActingUser(), CancellationToken.None);

        Assert.Equal((h.ShipmentId, ShipmentStatus.InInternationalTransit, "Released"), Assert.Single(h.Gateway.Overrides));
    }

    [Fact]
    public async Task NotVisible_ReturnsNullWithoutCallingTheGateway()
    {
        var h = await CreateHarnessAsync(new HashSet<Guid> { Guid.NewGuid() });
        await BookAsync(h);

        Assert.Null(await h.Service.HandoffAsync(h.ShipmentId, ActingUser(), CancellationToken.None));
        Assert.Null(await h.Service.OverrideStatusAsync(h.ShipmentId, new StatusOverrideRequest(ShipmentStatus.InInternationalTransit, "x"), ActingUser(), CancellationToken.None));
        Assert.Empty(h.Gateway.Handoffs);
        Assert.Empty(h.Gateway.Overrides);
    }
}
