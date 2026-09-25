using System.Security.Claims;
using Courier.International.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.International.Infrastructure.Carriers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Courier.International.Tests;

public class ManualCarrierEventServiceTests
{
    private sealed class Harness
    {
        public required CarrierTestDbContext DbContext { get; init; }
        public required Guid OrganizationId { get; init; }
        public required Guid ShipmentId { get; init; }
        public required InternationalLeg Leg { get; init; }
        public required RecordingJobScheduler Scheduler { get; init; }
        public required RecordingAuditLogger Audit { get; init; }
        public required ManualCarrierEventService Service { get; init; }
    }

    private static async Task<Harness> CreateHarnessAsync(IReadOnlySet<Guid>? accessibleBranchIds = null)
    {
        var dbContext = CarrierTestData.NewContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var leg = await CarrierTestData.SeedLegAsync(dbContext, organizationId, shipmentId);
        var scheduler = new RecordingJobScheduler();
        var audit = new RecordingAuditLogger();
        var service = new ManualCarrierEventService(dbContext, new FixedTenantProvider(organizationId), audit, new FixedShipmentTransitionService(),
            new FixedBranchAuthorizationChecker(accessibleBranchIds), new CarrierProviderRegistry(new ICarrierProvider[] { new ManualCarrierProvider() }),
            new CarrierTrackingEventWriter(dbContext, NullLogger<CarrierTrackingEventWriter>.Instance), scheduler);
        return new Harness { DbContext = dbContext, OrganizationId = organizationId, ShipmentId = shipmentId, Leg = leg, Scheduler = scheduler, Audit = audit, Service = service };
    }

    private static async Task BookManualAsync(Harness h)
    {
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "MANUAL");
        await CarrierTestData.SeedBookingAsync(h.DbContext, h.OrganizationId, h.Leg.Id, account.Id, CarrierBookingStatus.Booked, "MAN-1");
    }

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    [Fact]
    public async Task RecordAsync_KnownCode_StoresAManualPendingEvent()
    {
        var h = await CreateHarnessAsync();
        await BookManualAsync(h);
        var userId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow.AddHours(-1);

        var result = await h.Service.RecordAsync(h.ShipmentId, new ManualCarrierEventRequest(" in_transit ", occurredAt, "Delhi", "Departed hub"), ActingUser(userId), CancellationToken.None);

        Assert.Equal("Pending", result!.ProcessingStatus);
        Assert.Equal("IN_TRANSIT", result.CarrierStatusCode);
        var stored = await h.DbContext.Set<CarrierTrackingEvent>().SingleAsync();
        Assert.Equal(CarrierEventSource.Manual, stored.Source);
        Assert.Equal(userId, stored.EnteredByUserId);
        Assert.Equal("MAN-1", stored.CarrierAwb);
        Assert.Equal(1, h.Scheduler.ProcessingEnqueued);
        Assert.Contains(h.Audit.Entries, e => e.Action == "carrier_event.manual_recorded");
    }

    [Fact]
    public async Task RecordAsync_UnknownCode_IsRejected()
    {
        var h = await CreateHarnessAsync();
        await BookManualAsync(h);

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.RecordAsync(h.ShipmentId, new ManualCarrierEventRequest("TELEPORTED", DateTimeOffset.UtcNow, null, null), ActingUser(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_FutureOccurredAt_IsRejected()
    {
        var h = await CreateHarnessAsync();
        await BookManualAsync(h);

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.RecordAsync(h.ShipmentId, new ManualCarrierEventRequest("IN_TRANSIT", DateTimeOffset.UtcNow.AddHours(1), null, null), ActingUser(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_WithoutABookedBooking_IsRejected()
    {
        var h = await CreateHarnessAsync();

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.RecordAsync(h.ShipmentId, new ManualCarrierEventRequest("IN_TRANSIT", DateTimeOffset.UtcNow, null, null), ActingUser(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_NotVisible_ReturnsNull()
    {
        var h = await CreateHarnessAsync(new HashSet<Guid> { Guid.NewGuid() });
        await BookManualAsync(h);

        Assert.Null(await h.Service.RecordAsync(h.ShipmentId, new ManualCarrierEventRequest("IN_TRANSIT", DateTimeOffset.UtcNow, null, null), ActingUser(Guid.NewGuid()), CancellationToken.None));
    }
}
