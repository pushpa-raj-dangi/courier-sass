using System.Text.Json;
using Courier.International.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.International.Infrastructure.Carriers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Courier.International.Tests;

public class CarrierWebhookServiceTests
{
    private const string Secret = "whsec_test_secret";

    private sealed record Harness(CarrierTestDbContext DbContext, CarrierWebhookService Service, RecordingJobScheduler Scheduler, ICarrierSecretProtector Protector);

    private static Harness CreateHarness()
    {
        var dbContext = CarrierTestData.NewContext();
        var protector = new DataProtectionCarrierSecretProtector(new EphemeralDataProtectionProvider());
        var scheduler = new RecordingJobScheduler();
        var registry = new CarrierProviderRegistry(new ICarrierProvider[] { new TestCarrierProvider(), new ManualCarrierProvider() });
        var service = new CarrierWebhookService(dbContext, registry, protector,
            new CarrierTrackingEventWriter(dbContext, NullLogger<CarrierTrackingEventWriter>.Instance), scheduler, NullLogger<CarrierWebhookService>.Instance);
        return new Harness(dbContext, service, scheduler, protector);
    }

    private static Task<CarrierAccount> SeedWebhookAccountAsync(Harness h, Guid organizationId, bool isActive = true) =>
        CarrierTestData.SeedAccountAsync(h.DbContext, organizationId, "TESTCARRIER", isActive, encryptedWebhookSecret: h.Protector.ProtectWebhookSecret(Secret));

    private static Dictionary<string, string> SignedHeaders(string secret = Secret) =>
        new(StringComparer.OrdinalIgnoreCase) { [TestCarrierProvider.SignatureHeader] = secret };

    private static byte[] Body(params CarrierTrackingUpdate[] updates) =>
        JsonSerializer.SerializeToUtf8Bytes(updates, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static CarrierTrackingUpdate Update(string eventId) =>
        new("AWB-1", eventId, "TRANSIT", null, "Dubai", DateTimeOffset.UtcNow.AddMinutes(-5), null);

    [Fact]
    public async Task ReceiveAsync_ValidSignature_StoresEventsUnderTheAccountsTenant_AndEnqueuesProcessing()
    {
        var h = CreateHarness();
        var organizationId = Guid.NewGuid();
        var account = await SeedWebhookAccountAsync(h, organizationId);

        var outcome = await h.Service.ReceiveAsync(account.WebhookPublicId, SignedHeaders(), Body(Update("w1"), Update("w2")), CancellationToken.None);

        Assert.Equal(CarrierWebhookOutcome.Accepted, outcome);
        var stored = await h.DbContext.Set<CarrierTrackingEvent>().ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.All(stored, e => Assert.Equal(organizationId, e.OrganizationId));
        Assert.Equal(1, h.Scheduler.ProcessingEnqueued);
    }

    [Fact]
    public async Task ReceiveAsync_DuplicateDelivery_IsStoredOnce()
    {
        var h = CreateHarness();
        var account = await SeedWebhookAccountAsync(h, Guid.NewGuid());

        await h.Service.ReceiveAsync(account.WebhookPublicId, SignedHeaders(), Body(Update("w1")), CancellationToken.None);
        var again = await h.Service.ReceiveAsync(account.WebhookPublicId, SignedHeaders(), Body(Update("w1")), CancellationToken.None);

        Assert.Equal(CarrierWebhookOutcome.Accepted, again);
        Assert.Equal(1, await h.DbContext.Set<CarrierTrackingEvent>().CountAsync());
    }

    [Fact]
    public async Task ReceiveAsync_BadSignature_StoresNothing()
    {
        var h = CreateHarness();
        var account = await SeedWebhookAccountAsync(h, Guid.NewGuid());

        var outcome = await h.Service.ReceiveAsync(account.WebhookPublicId, SignedHeaders("wrong"), Body(Update("w1")), CancellationToken.None);

        Assert.Equal(CarrierWebhookOutcome.InvalidSignature, outcome);
        Assert.Empty(h.DbContext.Set<CarrierTrackingEvent>());
    }

    [Fact]
    public async Task ReceiveWebhook_ValidSignatureMalformedBody_ReturnsInvalidPayload()
    {
        var h = CreateHarness();
        var account = await SeedWebhookAccountAsync(h, Guid.NewGuid());

        var outcome = await h.Service.ReceiveAsync(account.WebhookPublicId, SignedHeaders(), "{not json"u8.ToArray(), CancellationToken.None);

        Assert.Equal(CarrierWebhookOutcome.InvalidPayload, outcome);
        Assert.Empty(h.DbContext.Set<CarrierTrackingEvent>());
    }

    [Fact]
    public async Task ReceiveAsync_UnknownInactiveOrWebhooklessAccount_IsNotFound()
    {
        var h = CreateHarness();
        var inactive = await SeedWebhookAccountAsync(h, Guid.NewGuid(), isActive: false);
        var manual = await CarrierTestData.SeedAccountAsync(h.DbContext, Guid.NewGuid(), "MANUAL");

        Assert.Equal(CarrierWebhookOutcome.NotFound, await h.Service.ReceiveAsync("no-such-id", SignedHeaders(), Body(Update("w1")), CancellationToken.None));
        Assert.Equal(CarrierWebhookOutcome.NotFound, await h.Service.ReceiveAsync(inactive.WebhookPublicId, SignedHeaders(), Body(Update("w1")), CancellationToken.None));
        Assert.Equal(CarrierWebhookOutcome.NotFound, await h.Service.ReceiveAsync(manual.WebhookPublicId, SignedHeaders(), Body(Update("w1")), CancellationToken.None));
    }
}
