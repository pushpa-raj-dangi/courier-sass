using Courier.International.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.International.Infrastructure.Carriers;
using Courier.SharedKernel;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Courier.International.Tests;

// Fix round 1, finding 2: one organization's poisoned carrier account (garbage credential
// ciphertext) must not abort the whole poll run for every other organization/booking.
//
// The manual carrier (the only ICarrierProvider registered in Program.cs today) does not support
// polling at all, so this cannot be exercised end-to-end through the real Api host yet (a fake
// polling-capable carrier is Task 15's concern). Instead this wires CarrierTrackingPollJob against
// real production collaborators (CarrierBookingPoller, CarrierProviderRegistry,
// DataProtectionCarrierSecretProtector, CarrierTrackingEventWriter) through a small
// ITenantScopeFactory fake — no production code changed to make this possible beyond the finding's
// own try/catch.
public class CarrierTrackingPollJobTests
{
    private sealed class FakeTenantScopeFactory : ITenantScopeFactory
    {
        private readonly Dictionary<Guid, IServiceProvider> _providers;
        public FakeTenantScopeFactory(Dictionary<Guid, IServiceProvider> providers) => _providers = providers;
        public AsyncServiceScope CreateScope(Guid organizationId) => _providers[organizationId].CreateAsyncScope();
    }

    private static IServiceProvider BuildOrgServiceProvider(DbContext dbContext, ICarrierProvider provider, ICarrierSecretProtector protector)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton<IShipmentTransitionService>(new FixedShipmentTransitionService { Status = ShipmentStatus.HandedToCarrier, Direction = ShipmentDirection.Outbound });
        services.AddSingleton<ICarrierProviderRegistry>(new CarrierProviderRegistry(new[] { provider }));
        services.AddSingleton(protector);
        services.AddSingleton(new CarrierTrackingEventWriter(dbContext, NullLogger<CarrierTrackingEventWriter>.Instance));
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<CarrierBookingPoller>>(NullLogger<CarrierBookingPoller>.Instance);
        services.AddTransient<CarrierBookingPoller>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RunAsync_OnePoisonedAccount_DoesNotStopAnotherOrganizationsBookingFromBeingPolled()
    {
        var dbContext = CarrierTestData.NewContext();
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        // Org A: garbage ciphertext — UnprotectCredentials throws inside PollAsync.
        var accountA = await CarrierTestData.SeedAccountAsync(dbContext, orgA, "TESTCARRIER", isActive: true, pollingEnabled: true,
            encryptedCredentials: "not-a-valid-protected-payload");
        var legA = await CarrierTestData.SeedLegAsync(dbContext, orgA, Guid.NewGuid());
        var bookingA = await CarrierTestData.SeedBookingAsync(dbContext, orgA, legA.Id, accountA.Id, CarrierBookingStatus.Booked, "POISON-AWB");

        // Org B: healthy account with a tracking update waiting to be polled.
        var accountB = await CarrierTestData.SeedAccountAsync(dbContext, orgB, "TESTCARRIER", isActive: true, pollingEnabled: true);
        var legB = await CarrierTestData.SeedLegAsync(dbContext, orgB, Guid.NewGuid());
        var bookingB = await CarrierTestData.SeedBookingAsync(dbContext, orgB, legB.Id, accountB.Id, CarrierBookingStatus.Booked, "GOOD-AWB");

        var providerB = new TestCarrierProvider();
        providerB.TrackingUpdates.Add(new CarrierTrackingUpdate("GOOD-AWB", "p1", "TRANSIT", null, null, DateTimeOffset.UtcNow, null));
        var protector = new DataProtectionCarrierSecretProtector(new EphemeralDataProtectionProvider());

        var scheduler = new RecordingJobScheduler();
        var scopeFactory = new FakeTenantScopeFactory(new Dictionary<Guid, IServiceProvider>
        {
            [orgA] = BuildOrgServiceProvider(dbContext, new TestCarrierProvider(), protector),
            [orgB] = BuildOrgServiceProvider(dbContext, providerB, protector)
        });
        var job = new CarrierTrackingPollJob(dbContext, scopeFactory, scheduler, NullLogger<CarrierTrackingPollJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        Assert.Empty(await dbContext.Set<CarrierTrackingEvent>().Where(e => e.OrganizationId == orgA).ToListAsync());
        var storedForB = await dbContext.Set<CarrierTrackingEvent>().Where(e => e.OrganizationId == orgB).ToListAsync();
        Assert.Single(storedForB);
        Assert.Equal(CarrierEventSource.Poll, storedForB[0].Source);
        Assert.Equal(1, scheduler.ProcessingEnqueued);
    }
}
