using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.International.Application;
using Courier.International.Domain;
using Courier.Persistence;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests.International;

public class CarrierHttpIntegrationTests : InternationalApiTestBase, IClassFixture<FakeCarrierAppFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly FakeCarrierBackend _backend;

    public CarrierHttpIntegrationTests(FakeCarrierAppFixture fixture) : base(fixture.Factory)
    {
        _backend = fixture.Backend;
        _backend.FailBookingWith = null;
    }

    private static async Task<CarrierAccountSecretResponse> FakeAccountAsync(HttpClient client, bool polling)
    {
        var response = await client.PostAsJsonAsync("/api/v1/international/carrier-accounts", new
        {
            carrierCode = FakeHttpCarrierProvider.Code, displayName = "Fake HTTP",
            credentials = new Dictionary<string, string> { ["apiKey"] = "fake-api-key-123" },
            pollingEnabled = polling, generateWebhookSecret = true
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CarrierAccountSecretResponse>())!;
    }

    private static Task<HttpResponseMessage> BookAsync(HttpClient client, Guid shipmentId, Guid accountId, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/international/shipments/{shipmentId}/book")
        {
            Content = JsonContent.Create(new { carrierAccountId = accountId, serviceLevel = "EXPRESS", carrierAwb = (string?)null })
        };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PostWebhookAsync(string webhookPath, string secret, params FakeTrackingEvent[] events)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(events, Json);
        var request = new HttpRequestMessage(HttpMethod.Post, webhookPath) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add(FakeHttpCarrierProvider.SignatureHeader, FakeHttpCarrierProvider.Sign(body, secret));
        return Factory.CreateClient().SendAsync(request);
    }

    private async Task<int> EventCountAsync(Guid accountId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<CarrierTrackingEvent>().IgnoreQueryFilters().CountAsync(e => e.CarrierAccountId == accountId);
    }

    [Fact]
    public async Task Outbound_BookOverHttp_Label_Handoff_SignedWebhooks_DeliverTheShipment()
    {
        var org = await SetUpOrgAsync("http-out");
        var account = await FakeAccountAsync(org.Client, polling: false);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);
        var id = shipment.Shipment.Id;

        var book = await BookAsync(org.Client, id, account.Account.Id, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, book.StatusCode);
        var booking = (await book.Content.ReadFromJsonAsync<CarrierBookingResponse>())!;
        Assert.StartsWith("FAKE", booking.CarrierAwb);
        Assert.True(booking.HasLabel);

        var label = await org.Client.GetAsync($"/api/v1/international/shipments/{id}/booking/label");
        Assert.Equal(HttpStatusCode.OK, label.StatusCode);
        Assert.Equal("%PDF-fake-label", await label.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await org.Client.PostAsync($"/api/v1/international/shipments/{id}/handoff", null)).StatusCode);

        var now = DateTimeOffset.UtcNow;
        var events = new[]
        {
            new FakeTrackingEvent(booking.CarrierAwb!, $"e-{Guid.NewGuid():N}", "DELIVERED", "Delivered", "London", now.AddMinutes(-1)),
            new FakeTrackingEvent(booking.CarrierAwb!, $"e-{Guid.NewGuid():N}", "TRANSIT", "Departed", "Kathmandu", now.AddHours(-5))
        };
        Assert.Equal(HttpStatusCode.Accepted, (await PostWebhookAsync(account.Account.WebhookPath, account.WebhookSecret!, events)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await PostWebhookAsync(account.Account.WebhookPath, account.WebhookSecret!, events)).StatusCode);
        Assert.Equal(2, await EventCountAsync(account.Account.Id));

        await ProcessCarrierEventsAsync(account.Account.Id);

        Assert.Equal(ShipmentStatus.DeliveredAbroad, await ShipmentStatusAsync(id));
    }

    [Fact]
    public async Task Webhook_BadSignature_Returns401_AndMalformedBody_Returns400_StoringNothing()
    {
        var org = await SetUpOrgAsync("http-sig");
        var account = await FakeAccountAsync(org.Client, polling: false);
        var evt = new FakeTrackingEvent("FAKE-X", "e-1", "TRANSIT", null, null, DateTimeOffset.UtcNow);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostWebhookAsync(account.Account.WebhookPath, "not-the-secret", evt)).StatusCode);

        var malformed = "{broken"u8.ToArray();
        var request = new HttpRequestMessage(HttpMethod.Post, account.Account.WebhookPath) { Content = new ByteArrayContent(malformed) };
        request.Headers.Add(FakeHttpCarrierProvider.SignatureHeader, FakeHttpCarrierProvider.Sign(malformed, account.WebhookSecret!));
        var response = await Factory.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CARRIER_WEBHOOK_INVALID_PAYLOAD", await response.Content.ReadAsStringAsync());

        Assert.Equal(0, await EventCountAsync(account.Account.Id));
    }

    [Fact]
    public async Task Booking_CarrierRejects_Returns502_ShipmentStaysCustomsPrepared_ThenANewKeyBooks()
    {
        var org = await SetUpOrgAsync("http-reject");
        var account = await FakeAccountAsync(org.Client, polling: false);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);
        _backend.FailBookingWith = HttpStatusCode.UnprocessableEntity;

        var rejected = await BookAsync(org.Client, shipment.Shipment.Id, account.Account.Id, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.BadGateway, rejected.StatusCode);
        var rejectedBody = await rejected.Content.ReadAsStringAsync();
        Assert.Contains("CARRIER_BOOKING_FAILED", rejectedBody);
        Assert.DoesNotContain("fake-api-key-123", rejectedBody);
        Assert.Equal(ShipmentStatus.CustomsPrepared, await ShipmentStatusAsync(shipment.Shipment.Id));

        _backend.FailBookingWith = null;
        Assert.Equal(HttpStatusCode.OK, (await BookAsync(org.Client, shipment.Shipment.Id, account.Account.Id, Guid.NewGuid().ToString("N"))).StatusCode);
    }

    [Fact]
    public async Task Booking_SameKeyTwice_CallsTheCarrierOnce()
    {
        var org = await SetUpOrgAsync("http-idem");
        var account = await FakeAccountAsync(org.Client, polling: false);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);
        var key = Guid.NewGuid().ToString("N");
        var callsBefore = _backend.BookCalls;

        var first = await (await BookAsync(org.Client, shipment.Shipment.Id, account.Account.Id, key)).Content.ReadFromJsonAsync<CarrierBookingResponse>();
        var second = await (await BookAsync(org.Client, shipment.Shipment.Id, account.Account.Id, key)).Content.ReadFromJsonAsync<CarrierBookingResponse>();

        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(callsBefore + 1, _backend.BookCalls);
    }

    [Fact]
    public async Task Inbound_LinkThenPoll_ReachesCustomsClearance_ThenReceive()
    {
        var org = await SetUpOrgAsync("http-in");
        var account = await FakeAccountAsync(org.Client, polling: true);
        var inbound = await CreateInboundAsync(org);
        var foreignAwb = $"DHLX{Guid.NewGuid():N}"[..16];
        _backend.TrackingByAwb[foreignAwb] = new List<FakeTrackingEvent>
        {
            new(foreignAwb, $"p-{Guid.NewGuid():N}", "TRANSIT", "Departed origin", "Frankfurt", DateTimeOffset.UtcNow.AddHours(-10)),
            new(foreignAwb, $"p-{Guid.NewGuid():N}", "CLEARANCE", "At customs", "Kathmandu", DateTimeOffset.UtcNow.AddHours(-1))
        };

        var link = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{inbound.Shipment.Id}/inbound-booking",
            new { carrierAccountId = account.Account.Id, carrierAwb = foreignAwb, serviceLevel = (string?)null });
        var booking = (await link.Content.ReadFromJsonAsync<CarrierBookingResponse>())!;

        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<CarrierTrackingPollJob>().PollBookingAsync(org.Auth.OrganizationId, booking.Id);
        }
        await ProcessCarrierEventsAsync(account.Account.Id);
        Assert.Equal(ShipmentStatus.CustomsClearance, await ShipmentStatusAsync(inbound.Shipment.Id));

        var receive = await org.Client.PostAsync($"/api/v1/international/shipments/{inbound.Shipment.Id}/receive", null);
        Assert.Equal(HttpStatusCode.OK, receive.StatusCode);
        Assert.Equal(ShipmentStatus.AtDestinationBranch, await ShipmentStatusAsync(inbound.Shipment.Id));
    }

    [Fact]
    public async Task RepeatedCarrierFailures_OpenTheCircuit_AndBookingReturns503()
    {
        var backend = new FakeCarrierBackend { FailBookingWith = HttpStatusCode.ServiceUnavailable };
        using var factory = FakeCarrierAppFixture.CreateFactory(backend, o =>
        {
            o.RetryBaseDelay = TimeSpan.Zero;
            o.MaxRetryAttempts = 1;
            o.MinimumThroughput = 2;
            o.SamplingDuration = TimeSpan.FromSeconds(60);
            o.BreakDuration = TimeSpan.FromSeconds(60);
        });
        var harness = new CircuitHarness(factory);
        var org = await harness.SetUpAsync("http-circuit");
        var account = await FakeAccountAsync(org.Client, polling: false);
        var shipment = await harness.PrepareAsync(org);

        HttpStatusCode last = default;
        for (var attempt = 0; attempt < 5 && last != HttpStatusCode.ServiceUnavailable; attempt++)
        {
            last = (await BookAsync(org.Client, shipment.Shipment.Id, account.Account.Id, Guid.NewGuid().ToString("N"))).StatusCode;
        }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, last);
    }

    // Reuses the base-class helpers against the circuit test's own factory.
    private sealed class CircuitHarness : InternationalApiTestBase
    {
        public CircuitHarness(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory) : base(factory) { }
        public Task<OrgContext> SetUpAsync(string label) => SetUpOrgAsync(label);
        public Task<InternationalShipmentDetailResponse> PrepareAsync(OrgContext org) => PrepareOutboundAtCustomsPreparedAsync(org);
    }
}
