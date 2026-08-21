using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Manifests.Application;
using Courier.Manifests.Domain;
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

public class ManifestsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public ManifestsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"Manifests Org {suffix}", $"manifests-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private static CreateBranchRequest ValidBranchRequest(string code) => new(
        code, $"Branch {code}", BranchType.Hub, KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
        "New Road, Kathmandu", null, null, null, null, null, null);

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

    private static List<Guid> KathmanduWard1Areas() => new()
    {
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area A"),
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area B"),
    };

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

    // Walks a freshly booked shipment through Riders' real pickup flow up to AtOriginBranch — the
    // point at which this phase's /manifests/{id}/scan takes over. Uses a throwaway rider created
    // solely to satisfy the now-real (Phase 8) pickup-assignment requirement; Manifests' own tests
    // don't otherwise care which rider performed the pickup.
    private async Task<ShipmentResponse> CreateShipmentAtOriginBranchAsync(HttpClient client, Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId)
    {
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();

        var riderResponse = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Pickup Rider", null, $"LIC-{Guid.NewGuid():N}"[..12], originBranchId, RiderRoleType.PickupRider));
        var rider = await riderResponse.Content.ReadFromJsonAsync<RiderResponse>();

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(rider!.Id, shipment!.Id, DeliveryTaskType.Pickup));
        await client.PostAsJsonAsync($"/api/v1/riders/{rider.Id}/scan/pickup", new RiderScanRequest(shipment.Awb));
        var afterOrigin = await (await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { })).Content.ReadFromJsonAsync<ShipmentResponse>();
        return afterOrigin!;
    }

    [Fact]
    public async Task FullManifestLifecycle_DraftToClosed_MovesShipmentThroughCustodyAndSurfacesMissingShipment()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "happy");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (fromBranchId, toBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);

        var received = await CreateShipmentAtOriginBranchAsync(client, merchantId, fromBranchId, toBranchId, serviceTypeId);
        var missing = await CreateShipmentAtOriginBranchAsync(client, merchantId, fromBranchId, toBranchId, serviceTypeId);

        var manifestResponse = await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId));
        Assert.Equal(HttpStatusCode.Created, manifestResponse.StatusCode);
        var manifest = await manifestResponse.Content.ReadFromJsonAsync<ManifestResponse>();
        Assert.Equal(ManifestStatus.Draft, manifest!.Status);

        var scanReceivedResponse = await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/scan", new ScanShipmentRequest(received.Awb));
        Assert.Equal(HttpStatusCode.OK, scanReceivedResponse.StatusCode);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/scan", new ScanShipmentRequest(missing.Awb));

        var afterScanShipment = await (await client.GetAsync($"/api/v1/shipments/{received.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Sorted, afterScanShipment!.CurrentStatus);

        var sealResponse = await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        var sealed_ = await sealResponse.Content.ReadFromJsonAsync<ManifestResponse>();
        Assert.Equal(ManifestStatus.Sealed, sealed_!.Status);
        Assert.Equal(2, sealed_.ShipmentCount);

        var dispatchResponse = await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        var dispatched = await dispatchResponse.Content.ReadFromJsonAsync<ManifestResponse>();
        Assert.Equal(ManifestStatus.InTransit, dispatched!.Status);
        Assert.Equal("BA 2 KHA 1234", dispatched.VehiclePlate);

        var afterDispatchShipment = await (await client.GetAsync($"/api/v1/shipments/{received.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.InTransit, afterDispatchShipment!.CurrentStatus);

        var receiveScanResponse = await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(received.Awb));
        Assert.Equal(HttpStatusCode.OK, receiveScanResponse.StatusCode);

        var afterReceiveShipment = await (await client.GetAsync($"/api/v1/shipments/{received.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.AtDestinationBranch, afterReceiveShipment!.CurrentStatus);
        Assert.Equal(toBranchId, afterReceiveShipment.CurrentBranchId);

        var closeResponse = await client.PostAsync($"/api/v1/manifests/{manifest.Id}/close", null);
        var closed = await closeResponse.Content.ReadFromJsonAsync<ManifestResponse>();
        Assert.Equal(ManifestStatus.Closed, closed!.Status);

        var missingResponse = await client.GetAsync($"/api/v1/manifests/{manifest.Id}/missing-shipments");
        var missingList = await missingResponse.Content.ReadFromJsonAsync<List<MissingShipmentResponse>>();
        var missingEntry = Assert.Single(missingList!);
        Assert.Equal(missing.Awb, missingEntry.Awb);

        // The missing shipment was dispatched along with the rest of the manifest (dispatch is a
        // bulk action that doesn't know yet which shipments will fail to be receive-scanned) — its
        // last known status is InTransit, not Sorted, since it genuinely left the origin branch
        // without ever being confirmed to arrive.
        var afterCloseMissingShipment = await (await client.GetAsync($"/api/v1/shipments/{missing.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.InTransit, afterCloseMissingShipment!.CurrentStatus);
    }

    [Fact]
    public async Task Scan_ShipmentStillCreated_Returns400WithWrongStateReason()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "wrong-state");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (fromBranchId, toBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, fromBranchId, toBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();

        var response = await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment!.Awb));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("MANIFEST_SCAN_REJECTED", body);
        Assert.Contains("WRONG_STATE", body);
    }

    [Fact]
    public async Task ReceiveScan_ShipmentNotOnThisManifest_Returns400WithNotOnManifestReason()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "not-on-manifest");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (fromBranchId, toBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var otherShipment = await CreateShipmentAtOriginBranchAsync(client, merchantId, fromBranchId, toBranchId, serviceTypeId);
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();

        // otherShipment was never scanned onto this manifest, so it's still AtOriginBranch — the
        // manifest itself is still Draft, which fails MANIFEST_NOT_RECEIVING before NOT_ON_MANIFEST
        // would ever be reached, so exercise the check with a manifest that IS receiving instead.
        var scannedShipment = await CreateShipmentAtOriginBranchAsync(client, merchantId, fromBranchId, toBranchId, serviceTypeId);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(scannedShipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));

        var response = await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(otherShipment.Awb));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("MANIFEST_SCAN_REJECTED", body);
        Assert.Contains("NOT_ON_MANIFEST", body);
    }

    [Fact]
    public async Task Manifests_OrgBCannotReadOrgAsManifest()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "iso-a");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var (fromBranchId, toBranchId, _) = await SeedBranchesAsync(clientA);
        var manifest = await (await clientA.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "iso-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var response = await clientB.GetAsync($"/api/v1/manifests/{manifest!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostManifests_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
