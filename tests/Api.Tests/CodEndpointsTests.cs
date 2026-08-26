using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Cod.Application;
using Courier.Cod.Domain;
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

public class CodEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public CodEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"Cod Org {suffix}", $"cod-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
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

    private async Task<Guid> CreateRiderAsync(HttpClient client, Guid branchId, RiderRoleType roleType)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-{suffix}@example.com", "P@ssw0rd123!", "Test Rider", null, $"LIC-{suffix[..10]}", branchId, roleType));
        var rider = await response.Content.ReadFromJsonAsync<RiderResponse>();
        return rider!.Id;
    }

    private static CreateShipmentRequest CodShipmentRequest(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, decimal codAmount) => new(
        merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, codAmount, false,
        "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
        "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 1, 500m) });

    // Books a COD shipment and walks it all the way to a completed COD delivery — pickup (Phase 8),
    // sort/dispatch/receive via Manifests (Phase 7), assign+complete delivery with
    // CollectedCodAmount (Phase 10, Task 6). Mirrors NdrEndpointsTests.cs's BookAndFailDeliveryAsync
    // shape exactly, just ending in a successful COD delivery instead of a failed attempt.
    private async Task<ShipmentResponse> BookAndCompleteCodDeliveryAsync(
        HttpClient client, Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId,
        Guid pickupRiderId, Guid deliveryRiderId, decimal codAmount)
    {
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", CodShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId, codAmount));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();

        // CodExpected is set at booking time, before any pickup/delivery activity — checked here,
        // before the rest of this helper walks the shipment through every later COD status.
        var atBooking = await GetCollectionForShipmentAsync(client, shipment!.Id);
        Assert.Equal(CodCollectionStatus.CodExpected, atBooking.Status);

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));
        await client.PostAsJsonAsync($"/api/v1/riders/{pickupRiderId}/scan/pickup", new RiderScanRequest(shipment.Awb));
        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { });

        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(shipment.Awb));

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));
        var completeResponse = await client.PostAsJsonAsync($"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete",
            new CompleteDeliveryRequest(shipment.Id, "Recipient One", true, null, null, null, null, codAmount));
        completeResponse.EnsureSuccessStatusCode();

        return (await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>())!;
    }

    private async Task<CodCollectionResponse> GetCollectionForShipmentAsync(HttpClient client, Guid shipmentId)
    {
        var collections = await (await client.GetAsync("/api/v1/cod/collections")).Content.ReadFromJsonAsync<List<CodCollectionResponse>>();
        return collections!.Single(c => c.ShipmentId == shipmentId);
    }

    [Fact]
    public async Task FullCodChain_BookingToMerchantPayout_MatchesWorkedExampleAndMovesThroughEveryStatus()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-success");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var shipment = await BookAndCompleteCodDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        Assert.Equal(ShipmentStatus.Delivered, shipment.CurrentStatus);

        var afterCollection = await GetCollectionForShipmentAsync(client, shipment.Id);
        Assert.Equal(CodCollectionStatus.CodCollected, afterCollection.Status);
        Assert.Equal(2500m, afterCollection.CollectedAmount);
        Assert.Equal(destinationBranchId, afterCollection.BranchId);

        var riderSettlement = await (await client.PostAsJsonAsync("/api/v1/cod/rider-settlements",
            new CreateRiderSettlementRequest(deliveryRiderId, destinationBranchId, 2500m))).Content.ReadFromJsonAsync<RiderCashSettlementResponse>();
        var afterRiderDeposit = await GetCollectionForShipmentAsync(client, shipment.Id);
        Assert.Equal(CodCollectionStatus.RiderDeposited, afterRiderDeposit.Status);

        var branchSettlement = await (await client.PostAsJsonAsync("/api/v1/cod/branch-settlements",
            new CreateBranchSettlementRequest(destinationBranchId, new List<Guid> { riderSettlement!.Id }, 2500m))).Content.ReadFromJsonAsync<BranchCashSettlementResponse>();
        var afterBranchReconcile = await GetCollectionForShipmentAsync(client, shipment.Id);
        Assert.Equal(CodCollectionStatus.BranchReconciled, afterBranchReconcile.Status);

        var financeVerifyResponse = await client.PostAsJsonAsync("/api/v1/cod/finance-verifications",
            new CreateFinanceVerificationRequest(branchSettlement!.Id, 2500m));
        financeVerifyResponse.EnsureSuccessStatusCode();
        var afterFinanceVerify = await GetCollectionForShipmentAsync(client, shipment.Id);
        Assert.Equal(CodCollectionStatus.FinanceVerified, afterFinanceVerify.Status);

        var merchantSettlementResponse = await client.PostAsJsonAsync("/api/v1/cod/merchant-settlements",
            new CreateMerchantSettlementRequest(merchantId, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)));
        merchantSettlementResponse.EnsureSuccessStatusCode();
        var merchantSettlement = await merchantSettlementResponse.Content.ReadFromJsonAsync<MerchantSettlementResponse>();
        var item = Assert.Single(merchantSettlement!.Items);
        // COD-LIFECYCLE.md §2's worked-example formula, proven against this test's own booked
        // shipment's actual locked-in TotalCharge rather than a hardcoded fee — robust to rate card
        // fixture changes while still proving CODAmount - CourierFee == NetPayable exactly.
        Assert.Equal(2500m, item.CODAmount);
        Assert.Equal(shipment.TotalCharge, item.CourierFee);
        Assert.Equal(2500m - shipment.TotalCharge, item.NetPayable);
        Assert.Equal(MerchantSettlementStatus.Created, merchantSettlement.Status);
        var afterSettlementCreated = await GetCollectionForShipmentAsync(client, shipment.Id);
        Assert.Equal(CodCollectionStatus.MerchantSettlementCreated, afterSettlementCreated.Status);

        var settleResponse = await client.PostAsync($"/api/v1/cod/merchant-settlements/{merchantSettlement.Id}/settle", null);
        settleResponse.EnsureSuccessStatusCode();
        var settled = await settleResponse.Content.ReadFromJsonAsync<MerchantSettlementResponse>();
        Assert.Equal(MerchantSettlementStatus.Settled, settled!.Status);
        Assert.NotNull(settled.SettledAt);
        var afterSettled = await GetCollectionForShipmentAsync(client, shipment.Id);
        Assert.Equal(CodCollectionStatus.Settled, afterSettled.Status);
    }

    [Fact]
    public async Task ConcurrentFinanceVerification_SameBranchCashSettlement_OnlyOneSucceeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-race");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var shipment = await BookAndCompleteCodDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var riderSettlement = await (await client.PostAsJsonAsync("/api/v1/cod/rider-settlements",
            new CreateRiderSettlementRequest(deliveryRiderId, destinationBranchId, 2500m))).Content.ReadFromJsonAsync<RiderCashSettlementResponse>();
        var branchSettlement = await (await client.PostAsJsonAsync("/api/v1/cod/branch-settlements",
            new CreateBranchSettlementRequest(destinationBranchId, new List<Guid> { riderSettlement!.Id }, 2500m))).Content.ReadFromJsonAsync<BranchCashSettlementResponse>();

        // Every request shares the same Authorization header on the same HttpClient — HttpClient is
        // safe for concurrent requests, and each HTTP request gets its own scoped DbContext via DI
        // (same reasoning Phase 5's rate-card concurrency test relies on).
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            client.PostAsJsonAsync("/api/v1/cod/finance-verifications", new CreateFinanceVerificationRequest(branchSettlement!.Id, 2500m))));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest));
        var afterRace = await GetCollectionForShipmentAsync(client, shipment.Id);
        Assert.Equal(CodCollectionStatus.FinanceVerified, afterRace.Status);
    }
}
