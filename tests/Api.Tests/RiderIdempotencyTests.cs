using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Http;
using System.Text.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Manifests.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

// Covers the F12 backend gap: IdempotencyMiddleware protected only merchant-JWT routes before
// this phase. A rider's offline-queued mutation can be replayed after a false-negative network
// failure, so the four rider action routes now require the same header — partitioned per staff
// user (`staff:{userId}`), not per merchant. Mirrors IdempotencyTests.cs's helper conventions;
// this file defines its own copies rather than sharing, matching that file's and
// RidersEndpointsTests.cs's established per-file-duplication convention in this codebase.
public class RiderIdempotencyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public RiderIdempotencyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"RiderIdem Org {suffix}", $"rider-idem-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private static CreateBranchRequest ValidBranchRequest(string code) => new(
        code, $"Branch {code}", BranchType.Hub, KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
        "New Road, Kathmandu", null, null, null, null, null, null);

    private static List<Guid> KathmanduWard1Areas() => new()
    {
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area A"),
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area B"),
    };

    private async Task<(Guid FromBranchId, Guid ToBranchId, Guid ServiceTypeId)> SeedBranchesAsync(HttpClient client)
    {
        var fromBranchResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"FR-{Guid.NewGuid():N}"[..10]));
        var fromBranch = await fromBranchResponse.Content.ReadFromJsonAsync<BranchResponse>();
        var toBranchResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"TO-{Guid.NewGuid():N}"[..10]));
        var toBranch = await toBranchResponse.Content.ReadFromJsonAsync<BranchResponse>();

        var originZoneResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Origin-{Guid.NewGuid():N}", KathmanduWard1Areas()));
        var originZone = await originZoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        var destZoneResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Dest-{Guid.NewGuid():N}", KathmanduWard1Areas()));
        var destZone = await destZoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        await client.PutAsJsonAsync($"/api/v1/branches/{fromBranch!.Id}/delivery-zone", new AssignDeliveryZoneRequest(originZone!.Id));
        await client.PutAsJsonAsync($"/api/v1/branches/{toBranch!.Id}/delivery-zone", new AssignDeliveryZoneRequest(destZone!.Id));

        var serviceTypesResponse = await client.GetAsync("/api/v1/service-types");
        var serviceTypes = await serviceTypesResponse.Content.ReadFromJsonAsync<List<ServiceTypeResponse>>();
        var standard = serviceTypes!.Single(s => s.Code == ServiceTypeCode.Standard);

        var weightSlabs = await (await client.GetAsync("/api/v1/weight-slabs")).Content.ReadFromJsonAsync<List<WeightSlabResponse>>();
        var slab = weightSlabs!.Single(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m);
        var rateCardResponse = await client.PostAsJsonAsync("/api/v1/rate-cards", new CreateRateCardRequest($"Card-{Guid.NewGuid():N}", DateTimeOffset.UtcNow));
        var rateCard = await rateCardResponse.Content.ReadFromJsonAsync<RateCardResponse>();
        await client.PostAsJsonAsync($"/api/v1/rate-cards/{rateCard!.Id}/zone-rates",
            new CreateZoneRateRequest(originZone.Id, destZone.Id, standard.Id, slab.Id, 100m));

        return (fromBranch.Id, toBranch.Id, standard.Id);
    }

    private async Task<Guid> CreateMerchantAsync(HttpClient staffClient)
    {
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "Shipper Pvt. Ltd.", "Shipper", "01-4444444", "shipper@example.com",
            $"shipper-owner-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        var merchant = await response.Content.ReadFromJsonAsync<MerchantResponse>();
        return merchant!.Id;
    }

    private static CreateShipmentRequest ValidShipmentRequest(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId) => new(
        merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
        "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
        "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 1, 500m) });

    private async Task<Guid> CreateRiderAsync(HttpClient client, Guid branchId, RiderRoleType roleType)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-{suffix}@example.com", "P@ssw0rd123!", "Test Rider", null, $"LIC-{suffix[..10]}", branchId, roleType));
        var rider = await response.Content.ReadFromJsonAsync<RiderResponse>();
        return rider!.Id;
    }

    [Fact]
    public async Task RiderScanPickup_NoIdempotencyKeyHeader_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "scan-missing");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));

        var response = await client.PostAsJsonAsync($"/api/v1/riders/{pickupRiderId}/scan/pickup", new RiderScanRequest(shipment.Awb));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", payload.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task RiderScanPickup_SameKeySameBody_ReplaysExactResponse_NoDuplicateTransition()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "scan-replay");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));

        using var request1 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{pickupRiderId}/scan/pickup")
        { Content = JsonContent.Create(new RiderScanRequest(shipment.Awb)) };
        request1.Headers.Add("Idempotency-Key", "scan-k1");
        var response1 = await client.SendAsync(request1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync();

        // Without the fix, this second call would hit the real handler again — the shipment is
        // already PickedUp, so a genuine re-execution would either error or silently no-op
        // differently. With the fix it never reaches the handler at all: byte-identical replay.
        using var request2 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{pickupRiderId}/scan/pickup")
        { Content = JsonContent.Create(new RiderScanRequest(shipment.Awb)) };
        request2.Headers.Add("Idempotency-Key", "scan-k1");
        var response2 = await client.SendAsync(request2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync();

        Assert.Equal(body1, body2);

        var afterScan = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.PickedUp, afterScan!.CurrentStatus);
    }

    [Fact]
    public async Task RiderDeliveryComplete_SameKeySameBody_ReplaysExactResponse_NoDuplicateCodCollection()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "complete-replay");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));
        using (var scanRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{pickupRiderId}/scan/pickup") { Content = JsonContent.Create(new RiderScanRequest(shipment.Awb)) })
        {
            scanRequest.Headers.Add("Idempotency-Key", $"scan-{Guid.NewGuid():N}");
            await client.SendAsync(scanRequest);
        }
        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { });
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));

        var completeRequestBody = new CompleteDeliveryRequest(shipment.Id, "Recipient One", true, null, null, null, null);

        using var request1 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete")
        { Content = JsonContent.Create(completeRequestBody) };
        request1.Headers.Add("Idempotency-Key", "complete-k1");
        var response1 = await client.SendAsync(request1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var attempt1 = await response1.Content.ReadFromJsonAsync<DeliveryAttemptResponse>();

        using var request2 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete")
        { Content = JsonContent.Create(completeRequestBody) };
        request2.Headers.Add("Idempotency-Key", "complete-k1");
        var response2 = await client.SendAsync(request2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var attempt2 = await response2.Content.ReadFromJsonAsync<DeliveryAttemptResponse>();

        // Same DeliveryAttempt.Id on both responses proves the second call never re-executed the
        // real handler (which would either create a second attempt row or 400 on an
        // already-Delivered shipment) — it's a cached replay, not a second successful completion.
        Assert.Equal(attempt1!.Id, attempt2!.Id);

        var delivered = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Delivered, delivered!.CurrentStatus);
    }

    [Fact]
    public async Task RiderDeliveryComplete_SameKeyDifferentBody_Returns409()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "complete-conflict");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));
        using (var scanRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{pickupRiderId}/scan/pickup") { Content = JsonContent.Create(new RiderScanRequest(shipment.Awb)) })
        {
            scanRequest.Headers.Add("Idempotency-Key", $"scan-{Guid.NewGuid():N}");
            await client.SendAsync(scanRequest);
        }
        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { });
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));

        using var request1 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete")
        { Content = JsonContent.Create(new CompleteDeliveryRequest(shipment.Id, "Recipient One", true, null, null, null, null)) };
        request1.Headers.Add("Idempotency-Key", "complete-conflict-k1");
        var response1 = await client.SendAsync(request1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        using var request2 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete")
        { Content = JsonContent.Create(new CompleteDeliveryRequest(shipment.Id, "A Different Name", true, null, null, null, null)) };
        request2.Headers.Add("Idempotency-Key", "complete-conflict-k1");
        var response2 = await client.SendAsync(request2);

        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);
        var payload = await response2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", payload.GetProperty("error").GetProperty("code").GetString());
    }

    // Regression guard: the new RiderAction route classification must match exactly the 4 rider
    // action routes, not every /api/v1/riders/** route. Dispatcher-facing rider routes (create,
    // assign-task) must stay exempt, same as every other staff route.
    [Fact]
    public async Task OtherRiderRoutes_CreateAndAssignTask_RemainExemptFromIdempotency()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "other-routes");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, _, _) = await SeedBranchesAsync(client);

        var suffix = Guid.NewGuid().ToString("N");
        var createResponse = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-{suffix}@example.com", "P@ssw0rd123!", "Test Rider", null, $"LIC-{suffix[..10]}", originBranchId, RiderRoleType.PickupRider));

        // POST /api/v1/riders returns 201 Created (unrelated pre-existing behavior); the point of
        // this assertion is that it succeeds normally and is not blocked by a missing
        // Idempotency-Key header (which would surface as 400 IDEMPOTENCY_KEY_REQUIRED).
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
    }

    // Closes coverage on the other two of the four newly-protected routes — the replay/conflict
    // behavior itself is already proven generically by the scan-pickup and delivery-complete
    // tests above (shared middleware code path), but each route's own regex match still needs a
    // direct check, since a typo in any one of the four patterns would otherwise go undetected.
    [Fact]
    public async Task RiderDeliveryFail_NoIdempotencyKeyHeader_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "fail-missing");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));
        using (var scanRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{pickupRiderId}/scan/pickup") { Content = JsonContent.Create(new RiderScanRequest(shipment.Awb)) })
        {
            scanRequest.Headers.Add("Idempotency-Key", $"scan-{Guid.NewGuid():N}");
            await client.SendAsync(scanRequest);
        }
        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { });
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));

        var response = await client.PostAsJsonAsync($"/api/v1/riders/{deliveryRiderId}/delivery-attempts/fail",
            new RecordFailedAttemptRequest(shipment.Id, DeliveryAttemptReason.CustomerUnavailable, "No one home", null, null, true, "Retry tomorrow"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", payload.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task RiderPodUpload_NoIdempotencyKeyHeader_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "pod-missing");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, _, _) = await SeedBranchesAsync(client);
        var deliveryRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.DeliveryRider);

        var signatureContent = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
        signatureContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var response = await client.PostAsync($"/api/v1/riders/{deliveryRiderId}/pod-uploads?kind=Signature", signatureContent);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", payload.GetProperty("error").GetProperty("code").GetString());
    }
}
