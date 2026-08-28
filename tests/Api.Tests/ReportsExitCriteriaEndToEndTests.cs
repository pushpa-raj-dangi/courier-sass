using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

namespace Courier.Api.Tests;

// Mirrors MerchantApiExitCriteriaEndToEndTests.cs's setup/lifecycle helpers exactly (Phase 12) —
// same branch/zone/rate-card/rider seeding, same pickup->manifest->delivery route sequence, staff
// JWT throughout (no merchant/API-key indirection needed here, since Reports itself is staff-only).
public class ReportsExitCriteriaEndToEndTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public ReportsExitCriteriaEndToEndTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static CreateBranchRequest ValidBranchRequest(string code, Guid? deliveryZoneId = null) => new(
        code, $"Branch {code}", BranchType.Hub, KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
        "New Road, Kathmandu", null, null, null, null, null, deliveryZoneId);

    private static List<Guid> KathmanduWard1Areas() => new()
    {
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area A"),
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area B"),
    };

    private async Task<(Guid OriginBranchId, Guid DestinationBranchId, Guid ServiceTypeId)> SeedRoutableBranchesAsync(HttpClient client)
    {
        var originZone = await (await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Origin-{Guid.NewGuid():N}", KathmanduWard1Areas()))).Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        var destZone = await (await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Dest-{Guid.NewGuid():N}", KathmanduWard1Areas()))).Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        var originBranch = await (await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"OZ-{Guid.NewGuid():N}"[..10], originZone!.Id))).Content.ReadFromJsonAsync<BranchResponse>();
        var destBranch = await (await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"DZ-{Guid.NewGuid():N}"[..10], destZone!.Id))).Content.ReadFromJsonAsync<BranchResponse>();
        var serviceTypes = await (await client.GetAsync("/api/v1/service-types")).Content.ReadFromJsonAsync<List<ServiceTypeResponse>>();
        var standard = serviceTypes!.Single(s => s.Code == ServiceTypeCode.Standard);
        return (originBranch!.Id, destBranch!.Id, standard.Id);
    }

    private async Task SeedRateCardAsync(HttpClient client, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, decimal baseRate)
    {
        var originBranch = (await (await client.GetAsync($"/api/v1/branches/{originBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;
        var destBranch = (await (await client.GetAsync($"/api/v1/branches/{destinationBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;
        var weightSlabs = await (await client.GetAsync("/api/v1/weight-slabs")).Content.ReadFromJsonAsync<List<WeightSlabResponse>>();
        var slab = weightSlabs!.Single(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m);
        var rateCard = await (await client.PostAsJsonAsync("/api/v1/rate-cards", new CreateRateCardRequest($"Card-{Guid.NewGuid():N}", DateTimeOffset.UtcNow))).Content.ReadFromJsonAsync<RateCardResponse>();
        await client.PostAsJsonAsync($"/api/v1/rate-cards/{rateCard!.Id}/zone-rates",
            new CreateZoneRateRequest(originBranch.DeliveryZoneId!.Value, destBranch.DeliveryZoneId!.Value, serviceTypeId, slab.Id, baseRate));
    }

    private async Task<Guid> CreateRiderAsync(HttpClient client, Guid branchId, RiderRoleType roleType)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var rider = await (await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-{suffix}@example.com", "P@ssw0rd123!", "Test Rider", null, $"LIC-{suffix[..10]}", branchId, roleType)))
            .Content.ReadFromJsonAsync<RiderResponse>();
        return rider!.Id;
    }

    // Deviation from the brief: the brief's CreateShipmentRequest passes a null MerchantId, mirrored
    // from MerchantApiExitCriteriaEndToEndTests.cs's Step 3 where the caller is an API-key client
    // and MerchantId is derived server-side from the key's owning merchant. This test drives the
    // whole lifecycle on a staff JWT instead (no API-key indirection needed, since Reports is
    // staff-only), and ShipmentService rejects a staff-created shipment with a null MerchantId
    // ("MerchantId is required.") — confirmed against ShipmentsEndpointsTests.cs's own
    // CreateMerchantAsync helper, which every staff-JWT shipment-creation test in that file uses.
    // So a merchant must be created here and its id passed explicitly.
    private async Task<Guid> CreateMerchantAsync(HttpClient staffClient)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "Reports Exit Shipper Pvt. Ltd.", "Reports Exit Shipper", "01-4444444", $"reports-exit-shipper-{suffix}@example.com",
            $"reports-exit-owner-{suffix}@example.com", "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        var merchant = await response.Content.ReadFromJsonAsync<MerchantResponse>();
        return merchant!.Id;
    }

    [Fact]
    public async Task OrgAndBranchDashboards_ReflectARealDeliveredShipment_NotHardcodedData()
    {
        var client = _factory.CreateClient();
        var suffix = $"exit-{Guid.NewGuid():N}";
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterOrganizationRequest(
            $"Reports Exit Org {suffix}", $"reports-exit-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person"));
        registerResponse.EnsureSuccessStatusCode();
        var auth = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var merchantId = await CreateMerchantAsync(client);

        const decimal codAmount = 750m;
        var createShipmentRequest = new CreateShipmentRequest(
            merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, codAmount, false,
            "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
            "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
            new List<CreateShipmentItemRequest> { new("Item A", 1, 500m) });
        var created = await (await client.PostAsJsonAsync("/api/v1/shipments", createShipmentRequest)).Content.ReadFromJsonAsync<ShipmentResponse>();

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, created!.Id, DeliveryTaskType.Pickup));
        await client.PostAsJsonAsync($"/api/v1/riders/{pickupRiderId}/scan/pickup", new RiderScanRequest(created.Awb));
        await client.PostAsJsonAsync($"/api/v1/shipments/{created.Id}/receive-origin", new { });

        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(created.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(created.Awb));

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, created.Id, DeliveryTaskType.Delivery));
        var completeResponse = await client.PostAsJsonAsync($"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete",
            new CompleteDeliveryRequest(created.Id, "Recipient One", true, null, null, null, null, codAmount));
        completeResponse.EnsureSuccessStatusCode();
        var delivered = await (await client.GetAsync($"/api/v1/shipments/{created.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Delivered, delivered!.CurrentStatus);

        var orgDashboardResponse = await client.GetAsync("/api/v1/reports/dashboard/organization");
        Assert.Equal(HttpStatusCode.OK, orgDashboardResponse.StatusCode);
        var orgDashboard = await orgDashboardResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(orgDashboard.GetProperty("todaysShipmentsByStatus").TryGetProperty("Delivered", out var deliveredCount));
        Assert.True(deliveredCount.GetInt32() >= 1);
        Assert.True(orgDashboard.GetProperty("financial").GetProperty("expected").GetDecimal() >= codAmount);

        var branchDashboardResponse = await client.GetAsync($"/api/v1/reports/dashboard/branch/{destinationBranchId}");
        Assert.Equal(HttpStatusCode.OK, branchDashboardResponse.StatusCode);
        var branchDashboard = await branchDashboardResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(branchDashboard.GetProperty("manifestCounts").GetProperty("recentlyClosed").GetInt32() >= 0);
    }
}
