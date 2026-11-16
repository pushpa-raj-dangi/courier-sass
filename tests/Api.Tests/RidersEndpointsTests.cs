using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Events.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Identity.Presentation;
using Courier.Manifests.Application;
using Courier.Merchants.Application;
using Courier.Persistence;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Reports.Application;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task PostLocation_LatitudeOutOfRange_Returns400()
    {
        // Latitude 1000 exceeds the numeric(9,6) column's 3-integer-digit range — without
        // server-side validation this would surface as an unhandled 500 from Postgres, not a 400.
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "location-ping-invalid");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, _, _) = await SeedBranchesAsync(client);
        var suffix = Guid.NewGuid().ToString("N");
        var riderEmail = $"rider-{suffix}@example.com";
        await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            riderEmail, "P@ssw0rd123!", "Bad Coord Rider", null, $"LIC-{suffix[..10]}", originBranchId, RiderRoleType.PickupRider));

        var riderClient = _factory.CreateClient();
        var loginResponse = await riderClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderEmail, "P@ssw0rd123!"));
        var riderAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        riderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuth!.AccessToken);

        var response = await riderClient.PostAsJsonAsync("/api/v1/riders/me/location",
            new LocationPingRequest(1000m, 85.3240m, 12.5m, DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetBranchLocations_AfterPing_ReturnsLatestPositionForActiveRider()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "branch-locations");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, _, _) = await SeedBranchesAsync(client);
        var suffix = Guid.NewGuid().ToString("N");
        var riderEmail = $"rider-{suffix}@example.com";
        var createResponse = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            riderEmail, "P@ssw0rd123!", "Branch Map Rider", null, $"LIC-{suffix[..10]}", originBranchId, RiderRoleType.PickupRider));
        var createdRider = await createResponse.Content.ReadFromJsonAsync<RiderResponse>();

        var riderClient = _factory.CreateClient();
        var loginResponse = await riderClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderEmail, "P@ssw0rd123!"));
        var riderAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        riderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuth!.AccessToken);
        await riderClient.PostAsJsonAsync("/api/v1/riders/me/location", new LocationPingRequest(27.7172m, 85.3240m, 10m, DateTimeOffset.UtcNow));

        var response = await client.GetAsync($"/api/v1/riders/branches/{originBranchId}/locations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var locations = await response.Content.ReadFromJsonAsync<List<RiderLocationResponse>>();
        var location = Assert.Single(locations!, l => l.RiderId == createdRider!.Id);
        Assert.Equal(27.7172m, location.Latitude);
    }

    [Fact]
    public async Task GetBranchLocations_OrgBBranch_Returns400()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "branch-locations-iso-a");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var (branchIdA, _, _) = await SeedBranchesAsync(clientA);

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "branch-locations-iso-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var response = await clientB.GetAsync($"/api/v1/riders/branches/{branchIdA}/locations");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetBranchLocations_RoleWithoutPermission_Returns403()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "branch-locations-perm");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, _, _) = await SeedBranchesAsync(client);
        // SortingStaff holds neither rider.read nor rider.location.read (AdditionalRoleSeeds.cs) —
        // the negative case for the RBAC fix in Task 2.
        var token = await InviteAcceptAndLoginForBranchAsync(client, "SortingStaff", originBranchId);
        var sortingStaffClient = _factory.CreateClient();
        sortingStaffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await sortingStaffClient.GetAsync($"/api/v1/riders/branches/{originBranchId}/locations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetBranchLocations_BranchManager_NowHasAccess()
    {
        // Proves Task 2's RBAC fix actually took effect end-to-end, not just declared in the seed array.
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "branch-locations-bm");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, _, _) = await SeedBranchesAsync(client);
        var token = await InviteAcceptAndLoginForBranchAsync(client, "BranchManager", originBranchId);
        var branchManagerClient = _factory.CreateClient();
        branchManagerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await branchManagerClient.GetAsync($"/api/v1/riders/branches/{originBranchId}/locations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetBranchLocations_Dispatcher_NowHasAccess()
    {
        // Same shape as GetBranchLocations_BranchManager_NowHasAccess above, proving the
        // rider.location.read RBAC fix also took effect end-to-end for Dispatcher.
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "branch-locations-dispatcher");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, _, _) = await SeedBranchesAsync(client);
        var token = await InviteAcceptAndLoginForBranchAsync(client, "Dispatcher", originBranchId);
        var dispatcherClient = _factory.CreateClient();
        dispatcherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await dispatcherClient.GetAsync($"/api/v1/riders/branches/{originBranchId}/locations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetRunBreadcrumbs_TwoPingsSameRun_ReturnsBothOrderedByTime()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "breadcrumbs");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var suffix = Guid.NewGuid().ToString("N");
        var riderEmail = $"rider-{suffix}@example.com";
        await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            riderEmail, "P@ssw0rd123!", "Breadcrumb Rider", null, $"LIC-{suffix[..10]}", originBranchId, RiderRoleType.PickupRider));
        var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();

        var riderClient = _factory.CreateClient();
        var loginResponse = await riderClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderEmail, "P@ssw0rd123!"));
        var riderAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        riderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuth!.AccessToken);

        // AssignTaskAsync (Task 8, Phase 8) auto-creates today's DeliveryRun — the only way to get a
        // real DeliveryRunId to attach pings to, matching how every other Riders test reaches a run.
        var riderRecord = await (await riderClient.GetAsync("/api/v1/riders/me")).Content.ReadFromJsonAsync<RiderResponse>();
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(riderRecord!.Id, shipment!.Id, DeliveryTaskType.Pickup));
        var tasksToday = await (await riderClient.GetAsync("/api/v1/riders/me/tasks/today")).Content.ReadFromJsonAsync<MyTasksTodayResponse>();
        var runId = tasksToday!.DeliveryRunId!.Value;

        await riderClient.PostAsJsonAsync("/api/v1/riders/me/location", new LocationPingRequest(27.70m, 85.30m, null, DateTimeOffset.UtcNow.AddMinutes(-2)));
        await riderClient.PostAsJsonAsync("/api/v1/riders/me/location", new LocationPingRequest(27.72m, 85.32m, null, DateTimeOffset.UtcNow));

        var response = await client.GetAsync($"/api/v1/riders/runs/{runId}/breadcrumbs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var breadcrumbs = await response.Content.ReadFromJsonAsync<List<RiderLocationResponse>>();
        Assert.Equal(2, breadcrumbs!.Count);
        Assert.Equal(27.70m, breadcrumbs[0].Latitude);
        Assert.Equal(27.72m, breadcrumbs[1].Latitude);
    }

    [Fact]
    public async Task GetRunBreadcrumbs_RoleWithoutPermission_Returns403()
    {
        // Mirrors GetBranchLocations_RoleWithoutPermission_Returns403's shape, targeting the
        // breadcrumbs endpoint instead. SortingStaff holds neither rider.read nor
        // rider.location.read (AdditionalRoleSeeds.cs).
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "breadcrumbs-perm");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var suffix = Guid.NewGuid().ToString("N");
        var riderEmail = $"rider-{suffix}@example.com";
        await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            riderEmail, "P@ssw0rd123!", "Perm Breadcrumb Rider", null, $"LIC-{suffix[..10]}", originBranchId, RiderRoleType.PickupRider));
        var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();

        var riderClient = _factory.CreateClient();
        var loginResponse = await riderClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderEmail, "P@ssw0rd123!"));
        var riderAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        riderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuth!.AccessToken);

        var riderRecord = await (await riderClient.GetAsync("/api/v1/riders/me")).Content.ReadFromJsonAsync<RiderResponse>();
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(riderRecord!.Id, shipment!.Id, DeliveryTaskType.Pickup));
        var tasksToday = await (await riderClient.GetAsync("/api/v1/riders/me/tasks/today")).Content.ReadFromJsonAsync<MyTasksTodayResponse>();
        var runId = tasksToday!.DeliveryRunId!.Value;

        var token = await InviteAcceptAndLoginForBranchAsync(client, "SortingStaff", originBranchId);
        var sortingStaffClient = _factory.CreateClient();
        sortingStaffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await sortingStaffClient.GetAsync($"/api/v1/riders/runs/{runId}/breadcrumbs");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetRunBreadcrumbs_RunFromAnotherOrganization_Returns404()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "breadcrumbs-iso-a");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var (originBranchIdA, destinationBranchIdA, serviceTypeIdA) = await SeedBranchesAsync(clientA);
        var merchantIdA = await CreateMerchantAsync(clientA);
        var suffixA = Guid.NewGuid().ToString("N");
        var riderEmailA = $"rider-{suffixA}@example.com";
        await clientA.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            riderEmailA, "P@ssw0rd123!", "Org A Rider", null, $"LIC-{suffixA[..10]}", originBranchIdA, RiderRoleType.PickupRider));
        var shipmentA = await (await clientA.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantIdA, originBranchIdA, destinationBranchIdA, serviceTypeIdA))).Content.ReadFromJsonAsync<ShipmentResponse>();
        var riderClientA = _factory.CreateClient();
        var loginA = await riderClientA.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderEmailA, "P@ssw0rd123!"));
        var riderAuthA = await loginA.Content.ReadFromJsonAsync<AuthResult>();
        riderClientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuthA!.AccessToken);
        var riderRecordA = await (await riderClientA.GetAsync("/api/v1/riders/me")).Content.ReadFromJsonAsync<RiderResponse>();
        await clientA.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(riderRecordA!.Id, shipmentA!.Id, DeliveryTaskType.Pickup));
        var tasksTodayA = await (await riderClientA.GetAsync("/api/v1/riders/me/tasks/today")).Content.ReadFromJsonAsync<MyTasksTodayResponse>();
        var runIdA = tasksTodayA!.DeliveryRunId!.Value;

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "breadcrumbs-iso-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var response = await clientB.GetAsync($"/api/v1/riders/runs/{runIdA}/breadcrumbs");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRunBreadcrumbs_UnknownRunId_Returns404()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "breadcrumbs-unknown");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync($"/api/v1/riders/runs/{Guid.NewGuid()}/breadcrumbs");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRunBreadcrumbs_BranchManagerOutsideRunBranch_Returns400()
    {
        // Fix-round regression test: a branch-scoped caller (BranchManager, who does hold
        // rider.location.read per Task 2's RBAC fix) denied access to the run's branch must get a
        // clean 400 RIDER_VALIDATION_FAILED from CheckBranchAccessAsync's RiderValidationException
        // — not an uncaught 500 — proving the endpoint mapping now routes through HandleAsync.
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "breadcrumbs-branch-scope");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var suffix = Guid.NewGuid().ToString("N");
        var riderEmail = $"rider-{suffix}@example.com";
        await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            riderEmail, "P@ssw0rd123!", "Scope Rider", null, $"LIC-{suffix[..10]}", originBranchId, RiderRoleType.PickupRider));
        var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();

        var riderClient = _factory.CreateClient();
        var loginResponse = await riderClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderEmail, "P@ssw0rd123!"));
        var riderAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        riderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuth!.AccessToken);

        var riderRecord = await (await riderClient.GetAsync("/api/v1/riders/me")).Content.ReadFromJsonAsync<RiderResponse>();
        // Pickup task -> DeliveryRun.BranchId is scanInfo.OriginBranchId (RiderService.Tasks.cs), so
        // this run belongs to originBranchId, not destinationBranchId.
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(riderRecord!.Id, shipment!.Id, DeliveryTaskType.Pickup));
        var tasksToday = await (await riderClient.GetAsync("/api/v1/riders/me/tasks/today")).Content.ReadFromJsonAsync<MyTasksTodayResponse>();
        var runId = tasksToday!.DeliveryRunId!.Value;

        // BranchManager scoped only to destinationBranchId — a different branch than the run's.
        var token = await InviteAcceptAndLoginForBranchAsync(client, "BranchManager", destinationBranchId);
        var branchManagerClient = _factory.CreateClient();
        branchManagerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await branchManagerClient.GetAsync($"/api/v1/riders/runs/{runId}/breadcrumbs");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- F15 R3b-2 reads: rider board, rider today, scoped rider single read, branch activity ----

    private async Task<(HttpClient Owner, Guid Origin, Guid Destination, Guid PickupRider, Guid DeliveryRider, ShipmentResponse Shipment)> SeedOutForDeliveryAsync(string label, bool assignDelivery = true)
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, label);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var shipment = await BookAndMoveToDestinationAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId);
        if (assignDelivery)
        {
            var assign = await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));
            assign.EnsureSuccessStatusCode();
        }
        return (client, originBranchId, destinationBranchId, pickupRiderId, deliveryRiderId, shipment);
    }

    private HttpClient ClientWithToken(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task RiderBoard_Owner_ReturnsTodaysRunCountsAndNames()
    {
        var (owner, origin, destination, pickupRiderId, deliveryRiderId, _) = await SeedOutForDeliveryAsync("board");

        var response = await owner.GetAsync($"/api/v1/riders/board?branchId={destination}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = Assert.Single((await response.Content.ReadFromJsonAsync<RiderBoardResponse>())!.Items);
        Assert.Equal(deliveryRiderId, row.RiderId);
        Assert.Equal("Test Rider", row.FullName);
        Assert.Equal(destination, row.BaseBranchId);
        Assert.Equal(DeliveryRunStatus.Active, row.Run!.Status);
        Assert.Equal(1, row.TasksAssigned);
        Assert.Equal(0, row.TasksCompleted);
        Assert.Null(row.LastPingAt);

        var all = (await owner.GetFromJsonAsync<RiderBoardResponse>("/api/v1/riders/board"))!.Items;
        Assert.Equal(2, all.Count);
        var pickupRow = all.Single(r => r.RiderId == pickupRiderId);
        Assert.Equal(origin, pickupRow.BaseBranchId);
        Assert.Equal(1, pickupRow.TasksCompleted);
    }

    [Fact]
    public async Task RiderBoardAndToday_WithoutRiderRead_Return403_AndAnonymous401()
    {
        var (owner, origin, _, _, deliveryRiderId, _) = await SeedOutForDeliveryAsync("board-perm", assignDelivery: false);
        var sortingStaff = ClientWithToken(await InviteAcceptAndLoginForBranchAsync(owner, "SortingStaff", origin));

        Assert.Equal(HttpStatusCode.Forbidden, (await sortingStaff.GetAsync("/api/v1/riders/board")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sortingStaff.GetAsync($"/api/v1/riders/{deliveryRiderId}/today")).StatusCode);

        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/riders/board")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/riders/{deliveryRiderId}/today")).StatusCode);
    }

    [Fact]
    public async Task RiderToday_InScope_ReturnsTasksWithAwbs()
    {
        var (owner, _, _, _, deliveryRiderId, shipment) = await SeedOutForDeliveryAsync("today");

        var response = await owner.GetAsync($"/api/v1/riders/{deliveryRiderId}/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var today = await response.Content.ReadFromJsonAsync<RiderTodayResponse>();
        Assert.Equal(deliveryRiderId, today!.Rider.RiderId);
        var task = Assert.Single(today.Tasks);
        Assert.Equal(shipment.Awb, task.Awb);
        Assert.Equal(DeliveryTaskType.Delivery, task.Type);
        Assert.Equal(DeliveryTaskStatus.Pending, task.Status);
    }

    [Fact]
    public async Task BranchScopedUser_SeesOnlyOwnBranchRiders_AndOtherBranchRiderReadsAre404()
    {
        var (owner, origin, destination, pickupRiderId, deliveryRiderId, _) = await SeedOutForDeliveryAsync("board-scope");
        var manager = ClientWithToken(await InviteAcceptAndLoginForBranchAsync(owner, "BranchManager", origin));

        var board = (await manager.GetFromJsonAsync<RiderBoardResponse>("/api/v1/riders/board"))!.Items;
        Assert.Equal(pickupRiderId, Assert.Single(board).RiderId);
        Assert.Empty((await manager.GetFromJsonAsync<RiderBoardResponse>($"/api/v1/riders/board?branchId={destination}"))!.Items);

        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/api/v1/riders/{pickupRiderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/v1/riders/{deliveryRiderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/v1/riders/{deliveryRiderId}/today")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/v1/riders/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/v1/riders/{Guid.NewGuid()}/today")).StatusCode);
    }

    [Fact]
    public async Task BranchActivity_Owner_CountsCustodyRidersOutAndExceptions()
    {
        var (owner, origin, destination, _, deliveryRiderId, shipment) = await SeedOutForDeliveryAsync("activity", assignDelivery: false);

        var before = (await owner.GetFromJsonAsync<BranchActivityResponse>("/api/v1/branches/activity"))!.Items;
        Assert.Equal(1, before.Single(r => r.BranchId == destination).OnHand);

        var assign = await owner.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));
        assign.EnsureSuccessStatusCode();

        var response = await owner.GetAsync("/api/v1/branches/activity");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = (await response.Content.ReadFromJsonAsync<BranchActivityResponse>())!.Items;
        var destinationRow = after.Single(r => r.BranchId == destination);
        Assert.Equal(0, destinationRow.OnHand);
        Assert.Equal(1, destinationRow.OutForDelivery);
        Assert.Equal(1, destinationRow.RidersOut);
        Assert.Equal(0, destinationRow.OpenExceptions);
        Assert.Equal(1, after.Single(r => r.BranchId == origin).RidersOut);
    }

    [Fact]
    public async Task BranchActivity_ScopedUserGetsOwnBranchRow_WithoutBranchRead403_Anonymous401()
    {
        var (owner, origin, _, _, _, _) = await SeedOutForDeliveryAsync("activity-scope");
        var manager = ClientWithToken(await InviteAcceptAndLoginForBranchAsync(owner, "BranchManager", origin));
        var sortingStaff = ClientWithToken(await InviteAcceptAndLoginForBranchAsync(owner, "SortingStaff", origin));

        var items = (await manager.GetFromJsonAsync<BranchActivityResponse>("/api/v1/branches/activity"))!.Items;
        Assert.Equal(origin, Assert.Single(items).BranchId);

        Assert.Equal(HttpStatusCode.Forbidden, (await sortingStaff.GetAsync("/api/v1/branches/activity")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/v1/branches/activity")).StatusCode);
    }

    [Fact]
    public async Task R3b2Reads_NeverExposeAnotherTenant()
    {
        var (_, originA, destinationA, pickupRiderA, deliveryRiderA, _) = await SeedOutForDeliveryAsync("tenant-a");
        var ownerB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(ownerB, "tenant-b");
        ownerB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var board = (await ownerB.GetFromJsonAsync<RiderBoardResponse>("/api/v1/riders/board"))!.Items;
        Assert.DoesNotContain(board, r => r.RiderId == pickupRiderA || r.RiderId == deliveryRiderA);
        Assert.Empty((await ownerB.GetFromJsonAsync<RiderBoardResponse>($"/api/v1/riders/board?branchId={originA}"))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.GetAsync($"/api/v1/riders/{deliveryRiderA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.GetAsync($"/api/v1/riders/{deliveryRiderA}/today")).StatusCode);
        var activity = (await ownerB.GetFromJsonAsync<BranchActivityResponse>("/api/v1/branches/activity"))!.Items;
        Assert.DoesNotContain(activity, r => r.BranchId == originA || r.BranchId == destinationA);
    }

    // ---- F15 R4: GET /riders/me/today ----

    private async Task<(Guid RiderId, HttpClient Client)> CreateRiderAndLoginAsync(HttpClient owner, Guid branchId, RiderRoleType roleType)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"rider-{suffix}@example.com";
        var response = await owner.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            email, "P@ssw0rd123!", "Asha Gurung", null, $"LIC-{suffix[..10]}", branchId, roleType));
        response.EnsureSuccessStatusCode();
        var rider = (await response.Content.ReadFromJsonAsync<RiderResponse>())!;
        var login = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "P@ssw0rd123!"));
        login.EnsureSuccessStatusCode();
        var auth = (await login.Content.ReadFromJsonAsync<AuthResult>())!;
        return (rider.Id, ClientWithToken(auth.AccessToken));
    }

    [Fact]
    public async Task MyToday_Rider_ReturnsOwnRunTasksAndCounts()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "my-today");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var (deliveryRiderId, riderClient) = await CreateRiderAndLoginAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var shipment = await BookAndMoveToDestinationAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId);
        (await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery))).EnsureSuccessStatusCode();

        var response = await riderClient.GetAsync("/api/v1/riders/me/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var today = (await response.Content.ReadFromJsonAsync<MyTodayResponse>())!;
        Assert.Equal(DeliveryRunStatus.Active, today.Run!.Status);
        var task = Assert.Single(today.Tasks);
        Assert.Equal(shipment.Awb, task.Awb);
        Assert.Equal(DeliveryTaskStatus.Pending, task.Status);
        Assert.Equal("Recipient One", task.RecipientName);
        Assert.Equal(new MyTodayCounts(1, 0, 0), today.Counts);
        Assert.Null(today.Cash);
        Assert.False(string.IsNullOrWhiteSpace(today.BranchName));
    }

    [Fact]
    public async Task MyToday_StaffUserWithoutRider_Returns404_Anonymous401()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "my-today-staff");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/riders/me/today")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/v1/riders/me/today")).StatusCode);
    }

    [Fact]
    public async Task MyToday_MerchantToken_Returns403()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "my-today-merchant");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var ownerEmail = $"merchant-owner-{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "Shipper Pvt. Ltd.", "Shipper", "01-4444444", "shipper@example.com",
            ownerEmail, "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"))).EnsureSuccessStatusCode();
        var login = await _factory.CreateClient().PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, "P@ssw0rd123!"));
        login.EnsureSuccessStatusCode();
        var merchant = ClientWithToken((await login.Content.ReadFromJsonAsync<MerchantAuthResult>())!.AccessToken);

        var status = (await merchant.GetAsync("/api/v1/riders/me/today")).StatusCode;

        Assert.True(status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized, $"Expected 403 (or 401 if merchant tokens are rejected by the staff scheme), got {(int)status}");
    }

    [Fact]
    public async Task MyToday_NeverExposesAnotherRidersTasks()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "my-today-isolation");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var assignedRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var (_, idleRiderClient) = await CreateRiderAndLoginAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var shipment = await BookAndMoveToDestinationAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId);
        (await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(assignedRiderId, shipment.Id, DeliveryTaskType.Delivery))).EnsureSuccessStatusCode();

        var today = (await idleRiderClient.GetFromJsonAsync<MyTodayResponse>("/api/v1/riders/me/today"))!;

        Assert.Null(today.Run);
        Assert.Empty(today.Tasks);
    }

    // Invites, accepts, and logs in a user under roleName scoped to the given branchId — same
    // invite/accept-invite/login chain as OrganizationSettingsEndpointsTests.InviteAcceptAndLoginAsync,
    // adapted to take an explicit branchId (that test's version always creates a fresh branch, but
    // branch-scope tests here need the invited user scoped to the SAME branch as the rider/location
    // under test).
    private async Task<string> InviteAcceptAndLoginForBranchAsync(HttpClient ownerClient, string roleName, Guid branchId)
    {
        var roles = await (await ownerClient.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
        var role = roles!.Single(r => r.Name == roleName);
        var email = $"staff-{Guid.NewGuid():N}@example.com";
        var password = "St@ffPassword1";

        var inviteResponse = await ownerClient.PostAsJsonAsync("/api/v1/users/invite",
            new InviteUserRequest(email, "Staff Person", null, role.Id, branchId, null));
        inviteResponse.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outboxMessage = await dbContext.Set<OutboxMessage>().IgnoreQueryFilters()
            .Where(m => m.EventType == "user.invited")
            .OrderByDescending(m => m.CreatedAt)
            .FirstAsync(m => m.Payload.Contains(email));
        var payload = System.Text.Json.JsonDocument.Parse(outboxMessage.Payload).RootElement;
        var inviteUrl = payload.GetProperty("InviteUrl").GetString()!;
        var token = Uri.UnescapeDataString(inviteUrl.Split("token=")[1]);

        var freshClient = _factory.CreateClient();
        var acceptResponse = await freshClient.PostAsJsonAsync("/api/v1/auth/accept-invite", new AcceptInviteRequest(token, password));
        acceptResponse.EnsureSuccessStatusCode();
        var acceptResult = await acceptResponse.Content.ReadFromJsonAsync<AuthResult>();
        return acceptResult!.AccessToken;
    }
}
