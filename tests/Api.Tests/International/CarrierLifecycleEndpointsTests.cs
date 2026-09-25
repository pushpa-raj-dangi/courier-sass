using System.Net;
using System.Net.Http.Json;
using Courier.International.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests.International;

public class CarrierLifecycleEndpointsTests : InternationalApiTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public CarrierLifecycleEndpointsTests(WebApplicationFactory<Program> factory) : base(factory) { }

    private sealed record ShipmentResponseForTest(Guid Id, string CurrentStatus, Guid? CurrentBranchId);

    private static async Task<Guid> ManualAccountAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/international/carrier-accounts",
            new { carrierCode = "MANUAL", displayName = "Manual", credentials = (object?)null, pollingEnabled = false, generateWebhookSecret = false });
        return (await response.Content.ReadFromJsonAsync<CarrierAccountSecretResponse>())!.Account.Id;
    }

    private static async Task BookManualAsync(HttpClient client, Guid shipmentId, Guid accountId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/international/shipments/{shipmentId}/book")
        {
            Content = JsonContent.Create(new { carrierAccountId = accountId, serviceLevel = (string?)null, carrierAwb = $"MAN-{Guid.NewGuid():N}"[..20] })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }

    private static Task<HttpResponseMessage> ManualEventAsync(HttpClient client, Guid shipmentId, string code) =>
        client.PostAsJsonAsync($"/api/v1/international/shipments/{shipmentId}/carrier-events",
            new { statusCode = code, occurredAt = DateTimeOffset.UtcNow.AddSeconds(-5), location = (string?)null, description = (string?)null });

    [Fact]
    public async Task Outbound_Book_Handoff_Hold_Override_Deliver()
    {
        var org = await SetUpOrgAsync("life-out");
        var accountId = await ManualAccountAsync(org.Client);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);
        var id = shipment.Shipment.Id;
        await BookManualAsync(org.Client, id, accountId);

        var handoff = await org.Client.PostAsync($"/api/v1/international/shipments/{id}/handoff", null);
        Assert.Equal(HttpStatusCode.OK, handoff.StatusCode);
        var handed = (await handoff.Content.ReadFromJsonAsync<ShipmentResponseForTest>())!;
        Assert.Equal("HandedToCarrier", handed.CurrentStatus);
        Assert.Null(handed.CurrentBranchId);

        Assert.Equal(HttpStatusCode.Accepted, (await ManualEventAsync(org.Client, id, "IN_TRANSIT")).StatusCode);
        await ProcessCarrierEventsAsync(accountId);
        Assert.Equal(HttpStatusCode.Accepted, (await ManualEventAsync(org.Client, id, "CUSTOMS_HOLD")).StatusCode);
        await ProcessCarrierEventsAsync(accountId);
        Assert.Equal(ShipmentStatus.CustomsHold, await ShipmentStatusAsync(id));

        var noReason = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{id}/status-override", new { targetStatus = "InInternationalTransit", reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        var badTarget = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{id}/status-override", new { targetStatus = "Delivered", reason = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, badTarget.StatusCode);
        var released = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{id}/status-override", new { targetStatus = "InInternationalTransit", reason = "Customs released after inspection" });
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);

        Assert.Equal(HttpStatusCode.Accepted, (await ManualEventAsync(org.Client, id, "DELIVERED")).StatusCode);
        await ProcessCarrierEventsAsync(accountId);
        Assert.Equal(ShipmentStatus.DeliveredAbroad, await ShipmentStatusAsync(id));

        var detail = await org.Client.GetFromJsonAsync<InternationalShipmentDetailResponse>($"/api/v1/international/shipments/{id}");
        Assert.NotNull(detail!.Booking!.CustodyStartedAt);
        Assert.Single(detail.BookingHistory!);
        Assert.Equal(3, detail.CarrierEvents!.Count(e => e.ProcessingStatus == "Applied"));
    }

    [Fact]
    public async Task Handoff_WithoutBooking_Returns400()
    {
        var org = await SetUpOrgAsync("life-nobook");
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);

        var response = await org.Client.PostAsync($"/api/v1/international/shipments/{shipment.Shipment.Id}/handoff", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Handoff_ByUserNotAtTheCurrentBranch_Returns400()
    {
        var org = await SetUpOrgAsync("life-branch");
        var accountId = await ManualAccountAsync(org.Client);
        var gatewayBranch = await CreateBranchAsync(org.Client, $"GW-{Guid.NewGuid():N}"[..10]);
        var shipment = await CreateOutboundAsync(org, org.BranchId, gatewayBranch);
        await SetShipmentStateAsync(shipment.Shipment.Id, ShipmentStatus.AtDestinationBranch, gatewayBranch);
        Assert.Equal(HttpStatusCode.OK, (await org.Client.PatchAsJsonAsync($"/api/v1/international/shipments/{shipment.Shipment.Id}/customs",
            CustomsBody(shipment.Shipment.Items.Single().Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await org.Client.PostAsync($"/api/v1/international/shipments/{shipment.Shipment.Id}/prepare-customs", null)).StatusCode);
        await BookManualAsync(org.Client, shipment.Shipment.Id, accountId);
        var originOnly = await SeedUserClientAsync(org.Auth, new[] { "international.handoff" }, org.BranchId);

        var response = await originOnly.PostAsync($"/api/v1/international/shipments/{shipment.Shipment.Id}/handoff", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SHIPMENT_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Inbound_Link_Transit_Customs_Receive_ArrivesAtTheSameBranch()
    {
        var org = await SetUpOrgAsync("life-in");
        var accountId = await ManualAccountAsync(org.Client);
        var inbound = await CreateInboundAsync(org);
        var id = inbound.Shipment.Id;
        Assert.Equal(HttpStatusCode.OK, (await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{id}/inbound-booking",
            new { carrierAccountId = accountId, carrierAwb = $"IN-{Guid.NewGuid():N}"[..20], serviceLevel = (string?)null })).StatusCode);

        Assert.Equal(HttpStatusCode.Accepted, (await ManualEventAsync(org.Client, id, "IN_TRANSIT")).StatusCode);
        await ProcessCarrierEventsAsync(accountId);
        Assert.Equal(HttpStatusCode.Accepted, (await ManualEventAsync(org.Client, id, "IN_CUSTOMS")).StatusCode);
        await ProcessCarrierEventsAsync(accountId);
        Assert.Equal(ShipmentStatus.CustomsClearance, await ShipmentStatusAsync(id));

        var receive = await org.Client.PostAsync($"/api/v1/international/shipments/{id}/receive", null);
        Assert.Equal(HttpStatusCode.OK, receive.StatusCode);
        Assert.Equal("AtDestinationBranch", (await receive.Content.ReadFromJsonAsync<ShipmentResponseForTest>())!.CurrentStatus);
    }

    [Fact]
    public async Task LifecycleRoutes_OtherTenant_Return404_AndMissingPermission_Returns403()
    {
        var owner = await SetUpOrgAsync("life-owner");
        var other = await SetUpOrgAsync("life-other");
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(owner);
        var id = shipment.Shipment.Id;

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsync($"/api/v1/international/shipments/{id}/handoff", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsync($"/api/v1/international/shipments/{id}/receive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync($"/api/v1/international/shipments/{id}/status-override",
            new { targetStatus = "InInternationalTransit", reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/v1/international/shipments/{id}/booking/label")).StatusCode);

        var readOnly = await SeedUserClientAsync(owner.Auth, new[] { "international.read" }, null);
        Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.PostAsync($"/api/v1/international/shipments/{id}/handoff", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.PostAsJsonAsync($"/api/v1/international/shipments/{id}/status-override",
            new { targetStatus = "InInternationalTransit", reason = "x" })).StatusCode);
        // readOnly holds only international.read, so it also lacks international.receive.
        Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.PostAsync($"/api/v1/international/shipments/{id}/receive", null)).StatusCode);

        // Fix round 1: a client missing international.read must be forbidden from the label route too.
        var handoffOnly = await SeedUserClientAsync(owner.Auth, new[] { "international.handoff" }, null);
        Assert.Equal(HttpStatusCode.Forbidden, (await handoffOnly.GetAsync($"/api/v1/international/shipments/{id}/booking/label")).StatusCode);
    }
}
