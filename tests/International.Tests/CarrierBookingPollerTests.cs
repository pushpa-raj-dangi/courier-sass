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

public class CarrierBookingPollerTests
{
    private sealed record Harness(CarrierTestDbContext DbContext, Guid OrganizationId, TestCarrierProvider Provider, FixedShipmentTransitionService Shipments, CarrierBookingPoller Poller);

    private static Harness CreateHarness()
    {
        var dbContext = CarrierTestData.NewContext();
        var provider = new TestCarrierProvider();
        var shipments = new FixedShipmentTransitionService { Status = ShipmentStatus.HandedToCarrier, Direction = ShipmentDirection.Outbound };
        var poller = new CarrierBookingPoller(dbContext, shipments, new CarrierProviderRegistry(new ICarrierProvider[] { provider }),
            new DataProtectionCarrierSecretProtector(new EphemeralDataProtectionProvider()),
            new CarrierTrackingEventWriter(dbContext, NullLogger<CarrierTrackingEventWriter>.Instance), NullLogger<CarrierBookingPoller>.Instance);
        return new Harness(dbContext, Guid.NewGuid(), provider, shipments, poller);
    }

    private static async Task<CarrierBooking> SeedAsync(Harness h, bool pollingEnabled = true, bool accountActive = true)
    {
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER", accountActive, pollingEnabled);
        var leg = await CarrierTestData.SeedLegAsync(h.DbContext, h.OrganizationId, Guid.NewGuid());
        return await CarrierTestData.SeedBookingAsync(h.DbContext, h.OrganizationId, leg.Id, account.Id, CarrierBookingStatus.Booked, "AWB-1");
    }

    [Fact]
    public async Task PollAsync_TrackedShipment_StoresTheCarriersEvents()
    {
        var h = CreateHarness();
        var booking = await SeedAsync(h);
        h.Provider.TrackingUpdates.Add(new CarrierTrackingUpdate("AWB-1", "p1", "TRANSIT", null, null, DateTimeOffset.UtcNow, null));

        var result = await h.Poller.PollAsync(booking.Id, CancellationToken.None);

        Assert.Equal(new CarrierPollResult(Polled: true, Inserted: 1), result);
        Assert.Equal(CarrierEventSource.Poll, (await h.DbContext.Set<CarrierTrackingEvent>().SingleAsync()).Source);
    }

    [Theory]
    [InlineData(ShipmentStatus.CustomsPrepared)]
    [InlineData(ShipmentStatus.DeliveredAbroad)]
    public async Task PollAsync_ShipmentOutsideTheTrackingWindow_IsSkipped(ShipmentStatus status)
    {
        var h = CreateHarness();
        var booking = await SeedAsync(h);
        h.Shipments.Status = status;

        Assert.Equal(new CarrierPollResult(false, 0), await h.Poller.PollAsync(booking.Id, CancellationToken.None));
    }

    [Fact]
    public async Task PollAsync_PollingDisabledOrInactiveAccount_IsSkipped()
    {
        var h = CreateHarness();
        var disabled = await SeedAsync(h, pollingEnabled: false);
        var inactive = await SeedAsync(h, accountActive: false);

        Assert.False((await h.Poller.PollAsync(disabled.Id, CancellationToken.None)).Polled);
        Assert.False((await h.Poller.PollAsync(inactive.Id, CancellationToken.None)).Polled);
    }

    [Fact]
    public async Task PollAsync_CarrierUnavailable_IsSwallowedAndNothingStored()
    {
        var h = CreateHarness();
        var booking = await SeedAsync(h);
        h.Provider.TrackingException = new BrokenCircuitException("open");

        var result = await h.Poller.PollAsync(booking.Id, CancellationToken.None);

        Assert.Equal(new CarrierPollResult(true, 0), result);
        Assert.Empty(h.DbContext.Set<CarrierTrackingEvent>());
    }

    // Ruling (task-11-brief override): the caller's own cancellation must propagate, not be
    // swallowed as a carrier failure — even though TaskCanceledException is itself one of
    // CarrierCallFailures.IsCarrierFailure's cases. The token is cancelled from inside the
    // provider call itself (via OnGetTrackingCalled), simulating a real HTTP call observing the
    // caller's own cancellation and throwing — not cancelled upfront, which would make the
    // in-memory EF provider throw its own OperationCanceledException on an earlier, unrelated
    // query before the poller ever reaches the provider.
    [Fact]
    public async Task PollAsync_CallerCancellation_PropagatesInsteadOfBeingSwallowed()
    {
        var h = CreateHarness();
        var booking = await SeedAsync(h);
        using var cts = new CancellationTokenSource();
        h.Provider.TrackingException = new TaskCanceledException();
        h.Provider.OnGetTrackingCalled = () => cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => h.Poller.PollAsync(booking.Id, cts.Token));
    }
}
