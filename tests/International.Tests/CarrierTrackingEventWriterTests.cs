using Courier.International.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Courier.International.Tests;

public class CarrierTrackingEventWriterTests
{
    private static CarrierTrackingUpdate Update(string eventId, string code = "TRANSIT", string awb = "AWB-1", string? description = null) =>
        new(awb, eventId, code, description, "Frankfurt", DateTimeOffset.UtcNow.AddHours(-1), "{\"k\":\"v\"}");

    [Fact]
    public async Task InsertAsync_StoresEventsUnderTheAccountsOrganizationAsPending()
    {
        await using var dbContext = CarrierTestData.NewContext();
        var organizationId = Guid.NewGuid();
        var account = await CarrierTestData.SeedAccountAsync(dbContext, organizationId, "TESTCARRIER");
        var writer = new CarrierTrackingEventWriter(dbContext, NullLogger<CarrierTrackingEventWriter>.Instance);

        var inserted = await writer.InsertAsync(account, new[] { Update("e1"), Update("e2") }, CarrierEventSource.Webhook, null, Array.Empty<string>(), CancellationToken.None);

        Assert.Equal(2, inserted);
        var stored = await dbContext.Set<CarrierTrackingEvent>().ToListAsync();
        Assert.All(stored, e =>
        {
            Assert.Equal(organizationId, e.OrganizationId);
            Assert.Equal(account.Id, e.CarrierAccountId);
            Assert.Equal(CarrierEventProcessingStatus.Pending, e.ProcessingStatus);
            Assert.Equal(CarrierEventSource.Webhook, e.Source);
        });
    }

    [Fact]
    public async Task InsertAsync_DuplicateEventIdAcrossSources_StoredOnce()
    {
        await using var dbContext = CarrierTestData.NewContext();
        var account = await CarrierTestData.SeedAccountAsync(dbContext, Guid.NewGuid(), "TESTCARRIER");
        var writer = new CarrierTrackingEventWriter(dbContext, NullLogger<CarrierTrackingEventWriter>.Instance);

        await writer.InsertAsync(account, new[] { Update("e1") }, CarrierEventSource.Webhook, null, Array.Empty<string>(), CancellationToken.None);
        var second = await writer.InsertAsync(account, new[] { Update("e1"), Update("e1") }, CarrierEventSource.Poll, null, Array.Empty<string>(), CancellationToken.None);

        Assert.Equal(0, second);
        Assert.Equal(1, await dbContext.Set<CarrierTrackingEvent>().CountAsync());
    }

    [Fact]
    public async Task InsertAsync_SkipsUnstorableUpdates_TruncatesLongText_AndMasksSecrets()
    {
        await using var dbContext = CarrierTestData.NewContext();
        var account = await CarrierTestData.SeedAccountAsync(dbContext, Guid.NewGuid(), "TESTCARRIER");
        var writer = new CarrierTrackingEventWriter(dbContext, NullLogger<CarrierTrackingEventWriter>.Instance);
        var withSecret = new CarrierTrackingUpdate("AWB-1", "e3", "TRANSIT", null, null, DateTimeOffset.UtcNow, "{\"token\":\"sk_live_12345\"}");

        var inserted = await writer.InsertAsync(account, new[]
        {
            Update("e1", awb: ""),                              // no AWB — skipped
            Update(new string('x', 201)),                       // event id too long — skipped
            Update("e2", description: new string('d', 1500)),   // description truncated
            withSecret
        }, CarrierEventSource.Webhook, null, new[] { "sk_live_12345" }, CancellationToken.None);

        Assert.Equal(2, inserted);
        var e2 = await dbContext.Set<CarrierTrackingEvent>().SingleAsync(e => e.CarrierEventId == "e2");
        Assert.Equal(1000, e2.Description!.Length);
        var e3 = await dbContext.Set<CarrierTrackingEvent>().SingleAsync(e => e.CarrierEventId == "e3");
        Assert.DoesNotContain("sk_live_12345", e3.RawPayload);
    }
}
