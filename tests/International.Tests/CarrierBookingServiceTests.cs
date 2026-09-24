using System.Security.Claims;
using Courier.International.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.International.Infrastructure.Carriers;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;

namespace Courier.International.Tests;

public class CarrierBookingServiceTests
{
    private sealed class Harness
    {
        public required CarrierTestDbContext DbContext { get; init; }
        public required Guid OrganizationId { get; init; }
        public required FixedShipmentTransitionService Shipments { get; init; }
        public required TestCarrierProvider Provider { get; init; }
        public required InMemoryObjectStorage Storage { get; init; }
        public required RecordingJobScheduler Scheduler { get; init; }
        public required RecordingAuditLogger Audit { get; init; }
        public required CarrierBookingService Service { get; init; }
        public required Guid ShipmentId { get; init; }
        public required InternationalLeg Leg { get; init; }
    }

    private static async Task<Harness> CreateHarnessAsync(IReadOnlySet<Guid>? accessibleBranchIds = null)
    {
        var dbContext = CarrierTestData.NewContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var leg = await CarrierTestData.SeedLegAsync(dbContext, organizationId, shipmentId);
        var shipments = new FixedShipmentTransitionService { Status = ShipmentStatus.CustomsPrepared, Direction = ShipmentDirection.Outbound };
        var provider = new TestCarrierProvider();
        var storage = new InMemoryObjectStorage();
        var scheduler = new RecordingJobScheduler();
        var audit = new RecordingAuditLogger();
        var service = new CarrierBookingService(dbContext, new FixedTenantProvider(organizationId), audit, shipments,
            new FixedBranchAuthorizationChecker(accessibleBranchIds),
            new CarrierProviderRegistry(new ICarrierProvider[] { provider, new ManualCarrierProvider() }),
            new DataProtectionCarrierSecretProtector(new EphemeralDataProtectionProvider()), storage, scheduler, NullLogger<CarrierBookingService>.Instance);
        return new Harness
        {
            DbContext = dbContext, OrganizationId = organizationId, Shipments = shipments, Provider = provider, Storage = storage,
            Scheduler = scheduler, Audit = audit, Service = service, ShipmentId = shipmentId, Leg = leg
        };
    }

    private static ClaimsPrincipal ActingUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    [Fact]
    public async Task BookAsync_Success_PersistsBookedBookingAndStoresLabel()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        h.Provider.Book = request => new CarrierBookingResult("TC-777", "EXPRESS", new CarrierLabel(new byte[] { 1, 2, 3 }, "application/pdf"), "{\"awb\":\"TC-777\"}");

        var result = await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, "EXPRESS", null), "key-1", ActingUser(), CancellationToken.None);

        Assert.Equal("Booked", result!.Status);
        Assert.Equal("TC-777", result.CarrierAwb);
        Assert.True(result.HasLabel);
        var stored = await h.DbContext.Set<CarrierBooking>().SingleAsync();
        Assert.Equal(h.Leg.Id, stored.InternationalLegId);
        Assert.Equal(CarrierBookingDirection.Outbound, stored.Direction);
        Assert.Equal("application/pdf", stored.LabelContentType);
        Assert.Equal(new byte[] { 1, 2, 3 }, h.Storage.Objects[stored.LabelStorageKey!].Content);
        Assert.Contains(h.Audit.Entries, e => e.Action == "carrier_booking.booked");
    }

    [Fact]
    public async Task BookAsync_SameIdempotencyKey_ReturnsTheSameBookingWithoutCallingTheCarrierAgain()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");

        var first = await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None);
        var second = await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None);

        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(1, h.Provider.BookCalls);
    }

    [Fact]
    public async Task BookAsync_ManualCarrier_RequiresTheCarrierAwb()
    {
        var h = await CreateHarnessAsync();
        var manual = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "MANUAL");

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(manual.Id, null, null), "key-1", ActingUser(), CancellationToken.None));
        var booked = await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(manual.Id, null, "MAN-1"), "key-2", ActingUser(), CancellationToken.None);
        Assert.Equal("MAN-1", booked!.CarrierAwb);
    }

    [Fact]
    public async Task BookAsync_CarrierAwbForANetworkCarrier_IsRejected()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, "X-1"), "key-1", ActingUser(), CancellationToken.None));
        Assert.Equal(0, h.Provider.BookCalls);
    }

    [Theory]
    [InlineData(ShipmentStatus.Sorted, ShipmentDirection.Outbound)]
    [InlineData(ShipmentStatus.HandedToCarrier, ShipmentDirection.Outbound)]
    [InlineData(ShipmentStatus.CustomsPrepared, ShipmentDirection.Inbound)]
    public async Task BookAsync_WrongStatusOrDirection_IsRejectedBeforeAnyWrite(ShipmentStatus status, ShipmentDirection direction)
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        h.Shipments.Status = status;
        h.Shipments.Direction = direction;

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None));
        Assert.Empty(h.DbContext.Set<CarrierBooking>());
        Assert.Equal(0, h.Provider.BookCalls);
    }

    [Fact]
    public async Task BookAsync_InactiveOrUnknownAccount_IsRejected()
    {
        var h = await CreateHarnessAsync();
        var inactive = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER", isActive: false);

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(inactive.Id, null, null), "key-1", ActingUser(), CancellationToken.None));
        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(Guid.NewGuid(), null, null), "key-2", ActingUser(), CancellationToken.None));
    }

    [Fact]
    public async Task BookAsync_ExistingLiveBooking_IsRejected()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None);

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-2", ActingUser(), CancellationToken.None));
    }

    [Fact]
    public async Task BookAsync_CarrierRejects_MarksFailedAndThrowsBookingFailed_AndReplayReturnsTheSameFailure()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        h.Provider.BookException = new CarrierBookingException("Destination postcode invalid");

        var ex = await Assert.ThrowsAsync<CarrierBookingFailedException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None));
        Assert.Contains("Destination postcode invalid", ex.Message);
        var stored = await h.DbContext.Set<CarrierBooking>().SingleAsync();
        Assert.Equal(CarrierBookingStatus.Failed, stored.Status);

        await Assert.ThrowsAsync<CarrierBookingFailedException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None));
        Assert.Equal(1, h.Provider.BookCalls);

        h.Provider.BookException = null;
        var retried = await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-2", ActingUser(), CancellationToken.None);
        Assert.Equal("Booked", retried!.Status);
    }

    [Fact]
    public async Task BookAsync_CircuitOpen_MarksFailedAndThrowsUnavailable()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        h.Provider.BookException = new BrokenCircuitException("open");

        await Assert.ThrowsAsync<CarrierUnavailableException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None));
        Assert.Equal(CarrierBookingStatus.Failed, (await h.DbContext.Set<CarrierBooking>().SingleAsync()).Status);
    }

    [Fact]
    public async Task BookAsync_NotVisibleToTheCaller_ReturnsNull()
    {
        var h = await CreateHarnessAsync(new HashSet<Guid> { Guid.NewGuid() });
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");

        Assert.Null(await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None));
        Assert.Equal(0, h.Provider.BookCalls);
    }

    [Fact]
    public async Task CancelAsync_BeforeHandoff_CancelsAtTheCarrier()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        var booked = await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None);

        var cancelled = await h.Service.CancelAsync(h.ShipmentId, ActingUser(), CancellationToken.None);

        Assert.Equal("Cancelled", cancelled!.Status);
        Assert.Contains(booked!.CarrierAwb, h.Provider.CancelledAwbs);
    }

    [Fact]
    public async Task CancelAsync_AfterHandoff_IsRejected()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None);
        h.Shipments.Status = ShipmentStatus.HandedToCarrier;

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() => h.Service.CancelAsync(h.ShipmentId, ActingUser(), CancellationToken.None));
        Assert.Empty(h.Provider.CancelledAwbs);
    }

    [Fact]
    public async Task LinkInboundAsync_CreatesBookedLink_SchedulesAPoll_AndRequeuesUnmatchedEvents()
    {
        var h = await CreateHarnessAsync();
        h.Shipments.Status = ShipmentStatus.Created;
        h.Shipments.Direction = ShipmentDirection.Inbound;
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER", pollingEnabled: true);
        var early = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, account.Id, "FOREIGN-1", "TRANSIT", DateTimeOffset.UtcNow, CarrierEventProcessingStatus.Unmatched);

        var linked = await h.Service.LinkInboundAsync(h.ShipmentId, new LinkInboundBookingRequest(account.Id, " FOREIGN-1 ", null), ActingUser(), CancellationToken.None);

        Assert.Equal("Booked", linked!.Status);
        Assert.Equal("Inbound", linked.Direction);
        Assert.Equal("FOREIGN-1", linked.CarrierAwb);
        Assert.Equal(0, h.Provider.BookCalls);
        Assert.Contains((h.OrganizationId, linked.Id), h.Scheduler.Polls);
        h.DbContext.ChangeTracker.Clear();
        Assert.Equal(CarrierEventProcessingStatus.Pending, (await h.DbContext.Set<CarrierTrackingEvent>().SingleAsync(e => e.Id == early.Id)).ProcessingStatus);
        Assert.True(h.Scheduler.ProcessingEnqueued > 0);
    }

    [Fact]
    public async Task LinkInboundAsync_OutboundShipment_IsRejected()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.LinkInboundAsync(h.ShipmentId, new LinkInboundBookingRequest(account.Id, "X-1", null), ActingUser(), CancellationToken.None));
    }
}
