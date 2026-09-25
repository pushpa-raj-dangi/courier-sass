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

    // Fix round 3, finding 1: after a successful booking (with a label, exercising both the
    // Booked-write and the label-write acceptance paths), the tracked entity must end up Unchanged
    // — never left dirty. If it were left Modified, some later, unrelated SaveChangesAsync on this
    // same scoped DbContext (the audit logger, a concurrent Cancel via a different code path) could
    // blindly resend stale column values and silently undo a real state change made elsewhere.
    [Fact]
    public async Task BookAsync_Success_LeavesTheTrackedEntityUnchanged()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        h.Provider.Book = request => new CarrierBookingResult("TC-777", "EXPRESS", new CarrierLabel(new byte[] { 1, 2, 3 }, "application/pdf"), "{\"ok\":true}");

        var result = await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, "EXPRESS", null), "key-1", ActingUser(), CancellationToken.None);

        Assert.Equal("Booked", result!.Status);
        var trackedBooking = await h.DbContext.Set<CarrierBooking>().SingleAsync(b => b.Id == result.Id);
        Assert.Equal(EntityState.Unchanged, h.DbContext.Entry(trackedBooking).State);
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

    // Fix round 1, finding 1: a client disconnecting mid-call (or any already-cancelled token)
    // must never leave a booking stuck Pending. Passing an already-cancelled token exercises every
    // internal DB call BookAsync makes; if any of them still used the caller's token, EF Core's
    // InMemory provider would throw OperationCanceledException before this assertion is reached.
    [Fact]
    public async Task BookAsync_CallerCancellation_NeverAbortsAnInFlightBooking()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), cts.Token);

        Assert.Equal("Booked", result!.Status);
        Assert.False(h.Provider.LastBookCancellationToken!.Value.CanBeCanceled);
        var stored = await h.DbContext.Set<CarrierBooking>().SingleAsync();
        Assert.Equal(CarrierBookingStatus.Booked, stored.Status);
    }

    // Fix round 1, finding 1: a Pending booking left behind by a crash between committing it and
    // calling the carrier must not block every future /book call forever. Once it is stale (older
    // than the 2-minute threshold), a new call — even under a different Idempotency-Key — resumes
    // it using its own stored key, rather than creating a second row.
    [Fact]
    public async Task BookAsync_StalePendingBooking_IsResumedWithItsOwnStoredIdempotencyKeyAndServiceLevel()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        var stale = new CarrierBooking
        {
            Id = Guid.NewGuid(), OrganizationId = h.OrganizationId, InternationalLegId = h.Leg.Id, CarrierAccountId = account.Id,
            Direction = CarrierBookingDirection.Outbound, Status = CarrierBookingStatus.Pending, IdempotencyKey = "stale-key",
            ServiceLevel = "STALE-EXPRESS",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5), UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        h.DbContext.Set<CarrierBooking>().Add(stale);
        await h.DbContext.SaveChangesAsync();

        // Fix round 2, finding v: the retry's own ServiceLevel ("NEW-LEVEL") must be ignored — the
        // resume must send the stale booking's stored one ("STALE-EXPRESS").
        var result = await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, "NEW-LEVEL", null), "new-key", ActingUser(), CancellationToken.None);

        Assert.Equal("Booked", result!.Status);
        Assert.Equal(stale.Id, result.Id);
        Assert.Equal("stale-key", h.Provider.LastBookIdempotencyKey);
        Assert.Equal("STALE-EXPRESS", result.ServiceLevel);
        var only = Assert.Single(await h.DbContext.Set<CarrierBooking>().ToListAsync());
        Assert.Equal(CarrierBookingStatus.Booked, only.Status);
        Assert.Equal(stale.Id, only.Id);
    }

    // Fix round 2, finding i: a same-key retry after a crash (not just a different-key retry) must
    // also resume a stale Pending booking, instead of being blocked by the exact-key replay branch
    // forever reporting "still in progress".
    [Fact]
    public async Task BookAsync_SameIdempotencyKeyRetryOfAStalePendingBooking_IsResumed()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        var stale = new CarrierBooking
        {
            Id = Guid.NewGuid(), OrganizationId = h.OrganizationId, InternationalLegId = h.Leg.Id, CarrierAccountId = account.Id,
            Direction = CarrierBookingDirection.Outbound, Status = CarrierBookingStatus.Pending, IdempotencyKey = "K",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5), UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        h.DbContext.Set<CarrierBooking>().Add(stale);
        await h.DbContext.SaveChangesAsync();

        var result = await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "K", ActingUser(), CancellationToken.None);

        Assert.Equal("Booked", result!.Status);
        Assert.Equal(stale.Id, result.Id);
        Assert.Equal("K", h.Provider.LastBookIdempotencyKey);
        var only = Assert.Single(await h.DbContext.Set<CarrierBooking>().ToListAsync());
        Assert.Equal(CarrierBookingStatus.Booked, only.Status);
        Assert.Equal(stale.Id, only.Id);
    }

    // Fix round 2, finding iii: if another (winning) attempt already recorded a real success for
    // this exact row before this attempt's own carrier call comes back with a failure, that late
    // failure must never overwrite the Booked row.
    [Fact]
    public async Task BookAsync_LateFailure_DoesNotOverwriteARowAlreadyBookedByAConcurrentAttempt()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        h.Provider.BookException = new CarrierBookingException("boom");
        h.Provider.OnBookAsyncCalled = () =>
        {
            // Simulate a concurrent flow that already completed this exact booking successfully
            // (e.g. a resume that won the finding-ii claim) between this call's carrier request
            // being sent and its (failing) result being persisted.
            var booking = h.DbContext.Set<CarrierBooking>().Single();
            booking.Status = CarrierBookingStatus.Booked;
            booking.CarrierAwb = "TC-WON-THE-RACE";
            booking.BookedAt = DateTimeOffset.UtcNow;
            booking.UpdatedAt = DateTimeOffset.UtcNow;
            h.DbContext.SaveChanges();
        };

        await Assert.ThrowsAsync<CarrierBookingFailedException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None));

        h.DbContext.ChangeTracker.Clear();
        var stored = await h.DbContext.Set<CarrierBooking>().SingleAsync();
        Assert.Equal(CarrierBookingStatus.Booked, stored.Status);
        Assert.Equal("TC-WON-THE-RACE", stored.CarrierAwb);
        Assert.Null(stored.FailureReason);
    }

    // Fix round 1, finding 1: a fresh (< 2 minute old) Pending booking still reports "in progress",
    // unchanged — only a stale one is resumed.
    [Fact]
    public async Task BookAsync_FreshPendingBooking_StillReportsInProgress()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        var fresh = new CarrierBooking
        {
            Id = Guid.NewGuid(), OrganizationId = h.OrganizationId, InternationalLegId = h.Leg.Id, CarrierAccountId = account.Id,
            Direction = CarrierBookingDirection.Outbound, Status = CarrierBookingStatus.Pending, IdempotencyKey = "fresh-key",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        h.DbContext.Set<CarrierBooking>().Add(fresh);
        await h.DbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "new-key", ActingUser(), CancellationToken.None));
        Assert.Equal(0, h.Provider.BookCalls);
    }

    // Fix round 1, finding 1: a stale Pending booking on a DIFFERENT carrier account than the new
    // request is a conflict staff must resolve by hand, not something the service can silently
    // resume or override.
    [Fact]
    public async Task BookAsync_StalePendingBookingOnADifferentCarrierAccount_IsRejected()
    {
        var h = await CreateHarnessAsync();
        var original = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        var other = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "MANUAL");
        var stale = new CarrierBooking
        {
            Id = Guid.NewGuid(), OrganizationId = h.OrganizationId, InternationalLegId = h.Leg.Id, CarrierAccountId = original.Id,
            Direction = CarrierBookingDirection.Outbound, Status = CarrierBookingStatus.Pending, IdempotencyKey = "stale-key",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5), UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        h.DbContext.Set<CarrierBooking>().Add(stale);
        await h.DbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(other.Id, null, "MAN-1"), "new-key", ActingUser(), CancellationToken.None));
    }

    // Fix round 1, finding 2: the carrier already assigned an AWB — a subsequent label-upload
    // failure must never lose track of it. The booking still ends Booked, just without a label.
    [Fact]
    public async Task BookAsync_LabelUploadFails_StillCompletesBookedWithoutALabel()
    {
        var dbContext = CarrierTestData.NewContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var leg = await CarrierTestData.SeedLegAsync(dbContext, organizationId, shipmentId);
        var shipments = new FixedShipmentTransitionService { Status = ShipmentStatus.CustomsPrepared, Direction = ShipmentDirection.Outbound };
        var provider = new TestCarrierProvider
        {
            Book = request => new CarrierBookingResult("TC-999", null, new CarrierLabel(new byte[] { 9 }, "application/pdf"), null)
        };
        var service = new CarrierBookingService(dbContext, new FixedTenantProvider(organizationId), new RecordingAuditLogger(), shipments,
            new FixedBranchAuthorizationChecker(null),
            new CarrierProviderRegistry(new ICarrierProvider[] { provider, new ManualCarrierProvider() }),
            new DataProtectionCarrierSecretProtector(new EphemeralDataProtectionProvider()), new ThrowingObjectStorage(),
            new RecordingJobScheduler(), NullLogger<CarrierBookingService>.Instance);
        var account = await CarrierTestData.SeedAccountAsync(dbContext, organizationId, "TESTCARRIER");

        var result = await service.BookAsync(shipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None);

        Assert.Equal("Booked", result!.Status);
        Assert.False(result.HasLabel);
        var stored = await dbContext.Set<CarrierBooking>().SingleAsync();
        Assert.Equal(CarrierBookingStatus.Booked, stored.Status);
        Assert.Equal("TC-999", stored.CarrierAwb);
        Assert.Null(stored.LabelStorageKey);
        Assert.Null(stored.LabelContentType);
    }

    private sealed class ThrowingObjectStorage : Courier.SharedKernel.IObjectStorage
    {
        public Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default) =>
            throw new IOException("Simulated object storage failure.");
        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
