using System.Net;
using System.Net.Http.Json;
using Courier.International.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests.International;

public class CarrierEventEndpointsTests : InternationalApiTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public CarrierEventEndpointsTests(WebApplicationFactory<Program> factory) : base(factory) { }

    private async Task<(OrgContext Org, Guid ShipmentId, Guid AccountId)> BookedAndHandedOverAsync(string label)
    {
        var org = await SetUpOrgAsync(label);
        var account = await org.Client.PostAsJsonAsync("/api/v1/international/carrier-accounts",
            new { carrierCode = "MANUAL", displayName = "Manual", credentials = (object?)null, pollingEnabled = false, generateWebhookSecret = false });
        var accountId = (await account.Content.ReadFromJsonAsync<CarrierAccountSecretResponse>())!.Account.Id;
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);
        var book = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/international/shipments/{shipment.Shipment.Id}/book")
        {
            Content = JsonContent.Create(new { carrierAccountId = accountId, serviceLevel = (string?)null, carrierAwb = $"MAN-{Guid.NewGuid():N}"[..20] })
        };
        book.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, (await org.Client.SendAsync(book)).StatusCode);
        // The handoff route arrives in Task 14; position the shipment directly here.
        await SetShipmentStateAsync(shipment.Shipment.Id, ShipmentStatus.HandedToCarrier, null);
        return (org, shipment.Shipment.Id, accountId);
    }

    [Fact]
    public async Task ManualEvent_IsAcceptedAndProcessedIntoTheShipmentStatus()
    {
        var (org, shipmentId, accountId) = await BookedAndHandedOverAsync("manual-evt");

        var response = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{shipmentId}/carrier-events",
            new { statusCode = "IN_TRANSIT", occurredAt = DateTimeOffset.UtcNow.AddMinutes(-10), location = "Delhi", description = "Departed" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Pending", (await response.Content.ReadFromJsonAsync<CarrierTrackingEventResponse>())!.ProcessingStatus);

        await ProcessCarrierEventsAsync(accountId);

        Assert.Equal(ShipmentStatus.InInternationalTransit, await ShipmentStatusAsync(shipmentId));
    }

    [Fact]
    public async Task ManualEvent_UnknownCode_Returns400()
    {
        var (org, shipmentId, _) = await BookedAndHandedOverAsync("manual-bad");

        var response = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{shipmentId}/carrier-events",
            new { statusCode = "TELEPORTED", occurredAt = DateTimeOffset.UtcNow, location = (string?)null, description = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INTERNATIONAL_SHIPMENT_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ManualEvent_OtherTenant_Returns404_AndWithoutPermission_Returns403()
    {
        var (org, shipmentId, _) = await BookedAndHandedOverAsync("manual-owner");
        var other = await SetUpOrgAsync("manual-other");
        var body = new { statusCode = "IN_TRANSIT", occurredAt = DateTimeOffset.UtcNow, location = (string?)null, description = (string?)null };

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync($"/api/v1/international/shipments/{shipmentId}/carrier-events", body)).StatusCode);
        var hubStaffLike = await SeedUserClientAsync(org.Auth, new[] { "international.read", "international.book" }, null);
        Assert.Equal(HttpStatusCode.Forbidden, (await hubStaffLike.PostAsJsonAsync($"/api/v1/international/shipments/{shipmentId}/carrier-events", body)).StatusCode);
    }

    [Fact]
    public async Task Webhook_UnknownPublicId_Returns404_WithoutAuthentication()
    {
        var anonymous = Factory.CreateClient();

        var response = await anonymous.PostAsync($"/api/v1/carrier-webhooks/{Guid.NewGuid():N}", new StringContent("[]"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
