using System.Net;
using System.Net.Http.Json;
using Courier.International.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests.International;

public class CarrierBookingEndpointsTests : InternationalApiTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public CarrierBookingEndpointsTests(WebApplicationFactory<Program> factory) : base(factory) { }

    private static async Task<Guid> CreateManualAccountAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/international/carrier-accounts",
            new { carrierCode = "MANUAL", displayName = "Manual", credentials = (object?)null, pollingEnabled = false, generateWebhookSecret = false });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CarrierAccountSecretResponse>())!.Account.Id;
    }

    private static Task<HttpResponseMessage> BookAsync(HttpClient client, Guid shipmentId, Guid accountId, string carrierAwb, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/international/shipments/{shipmentId}/book")
        {
            Content = JsonContent.Create(new { carrierAccountId = accountId, serviceLevel = (string?)null, carrierAwb })
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Book_Replay_Cancel_Rebook_ManualCarrier()
    {
        var org = await SetUpOrgAsync("book-flow");
        var accountId = await CreateManualAccountAsync(org.Client);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);
        var key = Guid.NewGuid().ToString("N");

        var first = await BookAsync(org.Client, shipment.Shipment.Id, accountId, "MAN-100", key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var booked = (await first.Content.ReadFromJsonAsync<CarrierBookingResponse>())!;
        Assert.Equal("Booked", booked.Status);
        Assert.Equal("MAN-100", booked.CarrierAwb);

        var replay = await BookAsync(org.Client, shipment.Shipment.Id, accountId, "MAN-100", key);
        Assert.Equal(booked.Id, (await replay.Content.ReadFromJsonAsync<CarrierBookingResponse>())!.Id);

        var cancel = await org.Client.PostAsync($"/api/v1/international/shipments/{shipment.Shipment.Id}/booking/cancel", null);
        Assert.Equal("Cancelled", (await cancel.Content.ReadFromJsonAsync<CarrierBookingResponse>())!.Status);

        var rebook = await BookAsync(org.Client, shipment.Shipment.Id, accountId, "MAN-101", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, rebook.StatusCode);
    }

    [Fact]
    public async Task Book_WithoutIdempotencyKey_Returns400()
    {
        var org = await SetUpOrgAsync("book-nokey");
        var accountId = await CreateManualAccountAsync(org.Client);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);

        var response = await BookAsync(org.Client, shipment.Shipment.Id, accountId, "MAN-1", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("IDEMPOTENCY_KEY_REQUIRED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Book_BeforeCustomsPrepared_Returns400()
    {
        var org = await SetUpOrgAsync("book-early");
        var accountId = await CreateManualAccountAsync(org.Client);
        var shipment = await CreateOutboundAsync(org);

        var response = await BookAsync(org.Client, shipment.Shipment.Id, accountId, "MAN-1", Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INTERNATIONAL_SHIPMENT_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LinkInbound_OnCreatedInboundShipment_Succeeds_AndOnOutbound_Returns400()
    {
        var org = await SetUpOrgAsync("link");
        var accountId = await CreateManualAccountAsync(org.Client);
        var inbound = await CreateInboundAsync(org);
        var outbound = await CreateOutboundAsync(org);

        var link = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{inbound.Shipment.Id}/inbound-booking",
            new { carrierAccountId = accountId, carrierAwb = "FOREIGN-1", serviceLevel = (string?)null });
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        Assert.Equal("Inbound", (await link.Content.ReadFromJsonAsync<CarrierBookingResponse>())!.Direction);

        var wrong = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{outbound.Shipment.Id}/inbound-booking",
            new { carrierAccountId = accountId, carrierAwb = "FOREIGN-2", serviceLevel = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
    }

    [Fact]
    public async Task OtherTenantsShipment_Returns404_AndMissingPermission_Returns403()
    {
        var owner = await SetUpOrgAsync("book-owner");
        var other = await SetUpOrgAsync("book-other");
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(owner);
        var otherAccount = await CreateManualAccountAsync(other.Client);

        var crossTenant = await BookAsync(other.Client, shipment.Shipment.Id, otherAccount, "MAN-1", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        var readOnly = await SeedUserClientAsync(owner.Auth, new[] { "international.read" }, null);
        var forbidden = await BookAsync(readOnly, shipment.Shipment.Id, Guid.NewGuid(), "MAN-1", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task UserScopedToAnotherBranch_Returns404()
    {
        var org = await SetUpOrgAsync("book-scope");
        var accountId = await CreateManualAccountAsync(org.Client);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);
        var otherBranch = await CreateBranchAsync(org.Client, $"OB-{Guid.NewGuid():N}"[..10]);
        var scoped = await SeedUserClientAsync(org.Auth, new[] { "international.book" }, otherBranch);

        var response = await BookAsync(scoped, shipment.Shipment.Id, accountId, "MAN-1", Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
