using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

public class RidersEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public RidersEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"Riders Org {suffix}", $"riders-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
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

    // Walks a shipment from Created all the way to AtDestinationBranch via the real pickup-rider
    // flow (Task 8) and Manifests' real scan/dispatch/receive flow (Phase 7) — the exact hand-off
    // point Riders' delivery-side flow (Task 9) takes over from.
    private async Task<ShipmentResponse> BookAndMoveToDestinationAsync(
        HttpClient client, Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, Guid pickupRiderId)
    {
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));
        using (var scanRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{pickupRiderId}/scan/pickup")
        { Content = JsonContent.Create(new RiderScanRequest(shipment.Awb)) })
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

        return (await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>())!;
    }

    [Fact]
    public async Task FullSuccessPath_PickupToDelivered_MovesCustodyThroughRiderAndCapturesPod()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "success");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var afterOrigin = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await afterOrigin.Content.ReadFromJsonAsync<ShipmentResponse>();

        var assignPickupResponse = await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));
        Assert.Equal(HttpStatusCode.OK, assignPickupResponse.StatusCode);
        var afterAssignPickup = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.PickupAssigned, afterAssignPickup!.CurrentStatus);
        Assert.Equal(pickupRiderId, afterAssignPickup.CurrentCustodianRiderId);

        using var scanRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{pickupRiderId}/scan/pickup")
        { Content = JsonContent.Create(new RiderScanRequest(shipment.Awb)) };
        scanRequest.Headers.Add("Idempotency-Key", $"scan-{Guid.NewGuid():N}");
        var scanResponse = await client.SendAsync(scanRequest);
        Assert.Equal(HttpStatusCode.OK, scanResponse.StatusCode);
        var afterScan = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.PickedUp, afterScan!.CurrentStatus);

        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { });
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(shipment.Awb));
        var atDestination = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.AtDestinationBranch, atDestination!.CurrentStatus);

        var assignDeliveryResponse = await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));
        Assert.Equal(HttpStatusCode.OK, assignDeliveryResponse.StatusCode);
        var afterAssignDelivery = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.OutForDelivery, afterAssignDelivery!.CurrentStatus);
        Assert.Equal(deliveryRiderId, afterAssignDelivery.CurrentCustodianRiderId);

        var signatureContent = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
        signatureContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/pod-uploads?kind=Signature")
        { Content = signatureContent };
        uploadRequest.Headers.Add("Idempotency-Key", $"pod-{Guid.NewGuid():N}");
        var uploadResponse = await client.SendAsync(uploadRequest);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadPodFileResponse>();

        using var completeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete")
        { Content = JsonContent.Create(new CompleteDeliveryRequest(shipment.Id, "Recipient One", true, uploadResult!.StorageKey, null, null, null)) };
        completeRequest.Headers.Add("Idempotency-Key", $"complete-{Guid.NewGuid():N}");
        var completeResponse = await client.SendAsync(completeRequest);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var attempt = await completeResponse.Content.ReadFromJsonAsync<DeliveryAttemptResponse>();
        Assert.True(attempt!.Succeeded);

        var delivered = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Delivered, delivered!.CurrentStatus);
        Assert.Null(delivered.CurrentCustodianRiderId);
    }

    [Fact]
    public async Task FullFailurePath_OutForDeliveryToNdrCreated_KeepsRiderCustodyAndRecordsAttempt()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "failure");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var shipment = await BookAndMoveToDestinationAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId);
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));

        using var failRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/delivery-attempts/fail")
        { Content = JsonContent.Create(new RecordFailedAttemptRequest(shipment.Id, DeliveryAttemptReason.CustomerUnavailable, "No one home", null, null, true, "Retry tomorrow")) };
        failRequest.Headers.Add("Idempotency-Key", $"fail-{Guid.NewGuid():N}");
        var failResponse = await client.SendAsync(failRequest);
        Assert.Equal(HttpStatusCode.OK, failResponse.StatusCode);
        var attempt = await failResponse.Content.ReadFromJsonAsync<DeliveryAttemptResponse>();
        Assert.False(attempt!.Succeeded);
        Assert.Equal(DeliveryAttemptReason.CustomerUnavailable, attempt.Reason);

        var afterFail = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.NdrCreated, afterFail!.CurrentStatus);
        // Custody stays with the rider — the parcel is still physically with them (Phase 9's design: NDR creation does not transfer custody).
        Assert.Equal(deliveryRiderId, afterFail.CurrentCustodianRiderId);
    }

    // Proves ShipmentConfiguration's xmin-based optimistic concurrency token (Phase 6) still
    // prevents a real double-delivery race through the new rider-driven completion endpoint —
    // supersedes Phase 6/7's Deliver_ConcurrentRequestsForSameShipment_ExactlyOneSucceeds (removed
    // in Task 2, since /shipments/{id}/deliver no longer exists). Two concurrent
    // POST /delivery-attempts/complete calls for the same shipment must not both succeed.
    [Fact]
    public async Task CompleteDelivery_ConcurrentRequestsForSameShipment_ExactlyOneSucceeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "deliver-race");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var shipment = await BookAndMoveToDestinationAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId);
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));

        // Distinct Idempotency-Key per concurrent request — this test targets the shipment's
        // xmin-based optimistic concurrency (the real handler-level race), not the idempotency
        // store's own dedup path, so the two calls must both actually reach the handler.
        var completeRequestBody = new CompleteDeliveryRequest(shipment.Id, "Recipient One", true, null, null, null, null);
        using var completeRequest1 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete")
        { Content = JsonContent.Create(completeRequestBody) };
        completeRequest1.Headers.Add("Idempotency-Key", $"complete-race-1-{Guid.NewGuid():N}");
        using var completeRequest2 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete")
        { Content = JsonContent.Create(completeRequestBody) };
        completeRequest2.Headers.Add("Idempotency-Key", $"complete-race-2-{Guid.NewGuid():N}");
        var task1 = client.SendAsync(completeRequest1);
        var task2 = client.SendAsync(completeRequest2);
        var responses = await Task.WhenAll(task1, task2);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);

        var final = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Delivered, final!.CurrentStatus);
    }

    [Fact]
    public async Task Riders_OrgBCannotReadOrgAsRider()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "iso-a");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var (originBranchId, _, _) = await SeedBranchesAsync(clientA);
        var riderId = await CreateRiderAsync(clientA, originBranchId, RiderRoleType.PickupRider);

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "iso-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var response = await clientB.GetAsync($"/api/v1/riders/{riderId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PickupRider_CannotAssignDeliveryTasks_Returns403()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "perm-scope");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var suffix = Guid.NewGuid().ToString("N");
        var riderEmail = $"rider-{suffix}@example.com";
        await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            riderEmail, "P@ssw0rd123!", "Pickup Only Rider", null, $"LIC-{suffix[..10]}", originBranchId, RiderRoleType.PickupRider));
        var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();

        var riderClient = _factory.CreateClient();
        var loginResponse = await riderClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderEmail, "P@ssw0rd123!"));
        var riderAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        riderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuth!.AccessToken);

        // PickupRider holds only shipment.scan (Task 3) — delivery.assign is DeliveryRider-only,
        // proving the branch-scoped role split is enforced at the HTTP layer, not just the
        // permission catalog.
        var response = await riderClient.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(Guid.NewGuid(), shipment!.Id, DeliveryTaskType.Delivery));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostRiders_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            "x@example.com", "P@ssw0rd123!", "X", null, "LIC-X", Guid.NewGuid(), RiderRoleType.PickupRider));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyRider_LoggedInAsRider_ReturnsOwnRecord()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "me-rider");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, _, _) = await SeedBranchesAsync(client);
        var suffix = Guid.NewGuid().ToString("N");
        var riderEmail = $"rider-{suffix}@example.com";
        var createResponse = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            riderEmail, "P@ssw0rd123!", "Self Service Rider", null, $"LIC-{suffix[..10]}", originBranchId, RiderRoleType.PickupRider));
        var createdRider = await createResponse.Content.ReadFromJsonAsync<RiderResponse>();

        var riderClient = _factory.CreateClient();
        var loginResponse = await riderClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderEmail, "P@ssw0rd123!"));
        var riderAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        riderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuth!.AccessToken);

        var response = await riderClient.GetAsync("/api/v1/riders/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RiderResponse>();
        Assert.Equal(createdRider!.Id, result!.Id);
    }

    [Fact]
    public async Task GetMyRider_LoggedInAsOwnerNotRider_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "me-nonrider");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync("/api/v1/riders/me");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMyTasksToday_AssignedDeliveryTask_ReturnsEnrichedPendingTask()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "my-tasks");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var suffix = Guid.NewGuid().ToString("N");
        var deliveryRiderEmail = $"rider-{suffix}@example.com";
        var deliveryRiderResponse = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            deliveryRiderEmail, "P@ssw0rd123!", "Delivery Rider", null, $"LIC-{suffix[..10]}", destinationBranchId, RiderRoleType.DeliveryRider));
        var deliveryRider = await deliveryRiderResponse.Content.ReadFromJsonAsync<RiderResponse>();

        var shipment = await BookAndMoveToDestinationAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId);
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRider!.Id, shipment.Id, DeliveryTaskType.Delivery));

        var riderClient = _factory.CreateClient();
        var loginResponse = await riderClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(deliveryRiderEmail, "P@ssw0rd123!"));
        var riderAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        riderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuth!.AccessToken);

        var response = await riderClient.GetAsync("/api/v1/riders/me/tasks/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<MyTasksTodayResponse>();
        var task = Assert.Single(result!.Tasks);
        Assert.Equal(shipment.Awb, task.Awb);
        Assert.Equal(DeliveryTaskType.Delivery, task.Type);
    }

    [Fact]
    public async Task GetMyTasksToday_TwoRidersSameOrg_EachSeesOnlyOwnTasks()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "my-tasks-isolation");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);

        var suffixA = Guid.NewGuid().ToString("N");
        var riderA = await (await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-a-{suffixA}@example.com", "P@ssw0rd123!", "Rider A", null, $"LIC-{suffixA[..10]}", destinationBranchId, RiderRoleType.DeliveryRider))).Content.ReadFromJsonAsync<RiderResponse>();
        var suffixB = Guid.NewGuid().ToString("N");
        var riderBEmail = $"rider-b-{suffixB}@example.com";
        await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            riderBEmail, "P@ssw0rd123!", "Rider B", null, $"LIC-{suffixB[..10]}", destinationBranchId, RiderRoleType.DeliveryRider));

        var shipment = await BookAndMoveToDestinationAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId);
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(riderA!.Id, shipment.Id, DeliveryTaskType.Delivery));

        var riderBClient = _factory.CreateClient();
        var loginB = await riderBClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderBEmail, "P@ssw0rd123!"));
        var authB = await loginB.Content.ReadFromJsonAsync<AuthResult>();
        riderBClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authB!.AccessToken);

        var response = await riderBClient.GetAsync("/api/v1/riders/me/tasks/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<MyTasksTodayResponse>();
        Assert.Null(result!.DeliveryRunId);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public async Task GetRidersSummary_ReturnsCounts()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "summary");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/riders/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GetRidersSummary_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/riders/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostLocation_LoggedInAsRider_RecordsPingAndReturns204()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "location-ping");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, _, _) = await SeedBranchesAsync(client);
        var suffix = Guid.NewGuid().ToString("N");
        var riderEmail = $"rider-{suffix}@example.com";
        await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            riderEmail, "P@ssw0rd123!", "Location Rider", null, $"LIC-{suffix[..10]}", originBranchId, RiderRoleType.PickupRider));

        var riderClient = _factory.CreateClient();
        var loginResponse = await riderClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderEmail, "P@ssw0rd123!"));
        var riderAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        riderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuth!.AccessToken);

        var response = await riderClient.PostAsJsonAsync("/api/v1/riders/me/location",
            new LocationPingRequest(27.7172m, 85.3240m, 12.5m, DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task PostLocation_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/riders/me/location",
            new LocationPingRequest(27.7172m, 85.3240m, null, DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
