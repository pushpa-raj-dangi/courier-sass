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
using Courier.SharedKernel;
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

    // Task 11: mirrors PickupsEndpointsTests.cs's CreateMerchantWithLoginAsync/LoginMerchantAsync —
    // the plain CreateMerchantAsync above only hands back the new merchant's Id (fine for the
    // staff-only settlement-creation flow), but the new merchant-facing GET /api/v1/cod/settlements
    // routes need an authenticated merchant JWT to call as.
    private async Task<(Guid MerchantId, string OwnerEmail, string Password)> CreateMerchantWithLoginAsync(HttpClient staffClient, string label)
    {
        var ownerEmail = $"cod-owner-{label}-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            $"Cod Shipper {label} Pvt. Ltd.", $"Cod Shipper {label}", "01-4444444", $"cod-shipper-{label}@example.com",
            ownerEmail, "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, $"Address {label}");
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", request);
        response.EnsureSuccessStatusCode();
        var merchant = await response.Content.ReadFromJsonAsync<MerchantResponse>();
        return (merchant!.Id, ownerEmail, "P@ssw0rd123!");
    }

    private async Task<HttpClient> LoginMerchantAsync(string ownerEmail, string password)
    {
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, password));
        loginResponse.EnsureSuccessStatusCode();
        var auth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return client;
    }

    // Books+delivers a COD shipment for the given merchant and walks the resulting COD collection
    // all the way through rider/branch/finance settlement into a Created MerchantSettlement, using
    // the existing staff-only creation flow proven by FullCodChain_... above. Used by Task 11's
    // merchant-facing settlement-read tests as a way to seed a real MerchantSettlement row.
    private async Task<MerchantSettlementResponse> SeedMerchantSettlementAsync(
        HttpClient staffClient, Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId,
        Guid pickupRiderId, Guid deliveryRiderId, decimal codAmount)
    {
        await BookAndCompleteCodDeliveryAsync(staffClient, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, codAmount);

        var riderSettlement = await (await staffClient.PostAsJsonAsync("/api/v1/cod/rider-settlements",
            new CreateRiderSettlementRequest(deliveryRiderId, destinationBranchId, codAmount))).Content.ReadFromJsonAsync<RiderCashSettlementResponse>();
        var branchSettlement = await (await staffClient.PostAsJsonAsync("/api/v1/cod/branch-settlements",
            new CreateBranchSettlementRequest(destinationBranchId, new List<Guid> { riderSettlement!.Id }, codAmount))).Content.ReadFromJsonAsync<BranchCashSettlementResponse>();
        var financeVerifyResponse = await staffClient.PostAsJsonAsync("/api/v1/cod/finance-verifications", new CreateFinanceVerificationRequest(branchSettlement!.Id, codAmount));
        financeVerifyResponse.EnsureSuccessStatusCode();

        var merchantSettlementResponse = await staffClient.PostAsJsonAsync("/api/v1/cod/merchant-settlements",
            new CreateMerchantSettlementRequest(merchantId, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)));
        merchantSettlementResponse.EnsureSuccessStatusCode();
        return (await merchantSettlementResponse.Content.ReadFromJsonAsync<MerchantSettlementResponse>())!;
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

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));
        using var completeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete")
        { Content = JsonContent.Create(new CompleteDeliveryRequest(shipment.Id, "Recipient One", true, null, null, null, null, codAmount)) };
        completeRequest.Headers.Add("Idempotency-Key", $"complete-{Guid.NewGuid():N}");
        var completeResponse = await client.SendAsync(completeRequest);
        completeResponse.EnsureSuccessStatusCode();

        return (await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>())!;
    }

    private async Task<CodCollectionResponse> GetCollectionForShipmentAsync(HttpClient client, Guid shipmentId)
    {
        var collections = await (await client.GetAsync("/api/v1/cod/collections?pageSize=200")).Content.ReadFromJsonAsync<PagedResult<CodCollectionResponse>>();
        return collections!.Items.Single(c => c.ShipmentId == shipmentId);
    }

    [Fact]
    public async Task ListCollections_Paginates_ReturnsDisjointPagesInNewestFirstOrder()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-list-page");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);

        var booked = new List<ShipmentResponse>();
        for (var i = 0; i < 3; i++)
        {
            var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", CodShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId, 500m))).Content.ReadFromJsonAsync<ShipmentResponse>();
            booked.Add(shipment!);
        }

        var page1 = await (await client.GetAsync("/api/v1/cod/collections?page=1&pageSize=2")).Content.ReadFromJsonAsync<PagedResult<CodCollectionResponse>>();
        var page2 = await (await client.GetAsync("/api/v1/cod/collections?page=2&pageSize=2")).Content.ReadFromJsonAsync<PagedResult<CodCollectionResponse>>();

        Assert.Equal(3, page1!.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2!.Items);

        var page1ShipmentIds = page1.Items.Select(c => c.ShipmentId).ToHashSet();
        var page2ShipmentIds = page2.Items.Select(c => c.ShipmentId).ToHashSet();
        Assert.Empty(page1ShipmentIds.Intersect(page2ShipmentIds));
        Assert.Equal(booked.Select(s => s.Id).ToHashSet(), page1ShipmentIds.Union(page2ShipmentIds).ToHashSet());

        var expectedNewestFirst = booked.OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id).Select(s => s.Id).ToList();
        Assert.Equal(expectedNewestFirst.Take(2), page1.Items.Select(c => c.ShipmentId));
        Assert.Equal(expectedNewestFirst.Skip(2), page2.Items.Select(c => c.ShipmentId));
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

    // ---- Frontend Phase F7: GET /rider-settlements, /branch-settlements, /adjustments ----

    [Fact]
    public async Task ListRiderSettlements_ReturnsCreatedSettlement_AndRespectsBranchScoping()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-list-rider");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        await BookAndCompleteCodDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var riderSettlement = await (await client.PostAsJsonAsync("/api/v1/cod/rider-settlements",
            new CreateRiderSettlementRequest(deliveryRiderId, destinationBranchId, 2500m))).Content.ReadFromJsonAsync<RiderCashSettlementResponse>();

        var listResponse = await client.GetAsync("/api/v1/cod/rider-settlements");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<PagedResult<RiderCashSettlementResponse>>();
        Assert.Contains(list!.Items, r => r.Id == riderSettlement!.Id);

        var unreconciledResponse = await client.GetAsync("/api/v1/cod/rider-settlements?unreconciledOnly=true");
        var unreconciledList = await unreconciledResponse.Content.ReadFromJsonAsync<PagedResult<RiderCashSettlementResponse>>();
        Assert.Contains(unreconciledList!.Items, r => r.Id == riderSettlement!.Id);

        // Reconciling this rider settlement into a branch settlement must remove it from the
        // unreconciledOnly view without removing it from the unfiltered list.
        await client.PostAsJsonAsync("/api/v1/cod/branch-settlements",
            new CreateBranchSettlementRequest(destinationBranchId, new List<Guid> { riderSettlement!.Id }, 2500m));
        var afterReconcileUnfiltered = await (await client.GetAsync("/api/v1/cod/rider-settlements")).Content.ReadFromJsonAsync<PagedResult<RiderCashSettlementResponse>>();
        Assert.Contains(afterReconcileUnfiltered!.Items, r => r.Id == riderSettlement.Id);
        var afterReconcileUnreconciledOnly = await (await client.GetAsync("/api/v1/cod/rider-settlements?unreconciledOnly=true")).Content.ReadFromJsonAsync<PagedResult<RiderCashSettlementResponse>>();
        Assert.DoesNotContain(afterReconcileUnreconciledOnly!.Items, r => r.Id == riderSettlement.Id);
    }

    [Fact]
    public async Task ListBranchSettlements_ReturnsCreatedSettlement_AndUnverifiedOnlyExcludesVerified()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-list-branch");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        await BookAndCompleteCodDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var riderSettlement = await (await client.PostAsJsonAsync("/api/v1/cod/rider-settlements",
            new CreateRiderSettlementRequest(deliveryRiderId, destinationBranchId, 2500m))).Content.ReadFromJsonAsync<RiderCashSettlementResponse>();
        var branchSettlement = await (await client.PostAsJsonAsync("/api/v1/cod/branch-settlements",
            new CreateBranchSettlementRequest(destinationBranchId, new List<Guid> { riderSettlement!.Id }, 2500m))).Content.ReadFromJsonAsync<BranchCashSettlementResponse>();

        var unverifiedResponse = await client.GetAsync("/api/v1/cod/branch-settlements?unverifiedOnly=true");
        var unverifiedList = await unverifiedResponse.Content.ReadFromJsonAsync<PagedResult<BranchCashSettlementResponse>>();
        Assert.Contains(unverifiedList!.Items, b => b.Id == branchSettlement!.Id);
        Assert.Equal(riderSettlement.Id, Assert.Single(unverifiedList.Items.Single(b => b.Id == branchSettlement!.Id).RiderCashSettlementIds));

        await client.PostAsJsonAsync("/api/v1/cod/finance-verifications", new CreateFinanceVerificationRequest(branchSettlement!.Id, 2500m));

        var afterVerifyUnverifiedOnly = await (await client.GetAsync("/api/v1/cod/branch-settlements?unverifiedOnly=true")).Content.ReadFromJsonAsync<PagedResult<BranchCashSettlementResponse>>();
        Assert.DoesNotContain(afterVerifyUnverifiedOnly!.Items, b => b.Id == branchSettlement.Id);
        var afterVerifyUnfiltered = await (await client.GetAsync("/api/v1/cod/branch-settlements")).Content.ReadFromJsonAsync<PagedResult<BranchCashSettlementResponse>>();
        Assert.Contains(afterVerifyUnfiltered!.Items, b => b.Id == branchSettlement.Id);
    }

    [Fact]
    public async Task ListAdjustments_ReturnsRequestedAdjustment_AndAppliedFilterSplitsPendingFromApproved()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-list-adjustments");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var (merchantId, _, _) = await CreateMerchantWithLoginAsync(client, "list-adjustments");
        var settlement = await SeedMerchantSettlementAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var item = settlement.Items.Single();

        var adjustmentResponse = await client.PostAsJsonAsync("/api/v1/cod/adjustments",
            new RequestAdjustmentRequest("MerchantSettlementItem", item.Id, 100m, "Damaged parcel deduction"));
        adjustmentResponse.EnsureSuccessStatusCode();
        var adjustment = await adjustmentResponse.Content.ReadFromJsonAsync<AdjustmentResponse>();

        var pendingList = await (await client.GetAsync("/api/v1/cod/adjustments?applied=false")).Content.ReadFromJsonAsync<PagedResult<AdjustmentResponse>>();
        Assert.Contains(pendingList!.Items, a => a.Id == adjustment!.Id);

        await client.PostAsync($"/api/v1/cod/adjustments/{adjustment!.Id}/approve", null);

        var afterApprovePending = await (await client.GetAsync("/api/v1/cod/adjustments?applied=false")).Content.ReadFromJsonAsync<PagedResult<AdjustmentResponse>>();
        Assert.DoesNotContain(afterApprovePending!.Items, a => a.Id == adjustment.Id);
        var afterApproveApplied = await (await client.GetAsync("/api/v1/cod/adjustments?applied=true")).Content.ReadFromJsonAsync<PagedResult<AdjustmentResponse>>();
        Assert.Contains(afterApproveApplied!.Items, a => a.Id == adjustment.Id);

        // merchantSettlementId narrows to this settlement's item adjustments only (F15 R3a I2).
        var forSettlement = await (await client.GetAsync($"/api/v1/cod/adjustments?applied=true&merchantSettlementId={settlement.Id}")).Content.ReadFromJsonAsync<PagedResult<AdjustmentResponse>>();
        var only = Assert.Single(forSettlement!.Items);
        Assert.Equal(adjustment.Id, only.Id);
        var forUnknownSettlement = await (await client.GetAsync($"/api/v1/cod/adjustments?merchantSettlementId={Guid.NewGuid()}")).Content.ReadFromJsonAsync<PagedResult<AdjustmentResponse>>();
        Assert.Empty(forUnknownSettlement!.Items);
    }

    [Fact]
    public async Task ApproveAdjustment_AgainstAlreadySettledMerchantSettlement_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-adjust-settled");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var (merchantId, _, _) = await CreateMerchantWithLoginAsync(client, "adjust-settled");
        var settlement = await SeedMerchantSettlementAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var item = settlement.Items.Single();
        var adjustment = await (await client.PostAsJsonAsync("/api/v1/cod/adjustments",
            new RequestAdjustmentRequest("MerchantSettlementItem", item.Id, 100m, "Damaged parcel deduction"))).Content.ReadFromJsonAsync<AdjustmentResponse>();

        var settleResponse = await client.PostAsync($"/api/v1/cod/merchant-settlements/{settlement.Id}/settle", null);
        settleResponse.EnsureSuccessStatusCode();

        var approveResponse = await client.PostAsync($"/api/v1/cod/adjustments/{adjustment!.Id}/approve", null);

        Assert.Equal(HttpStatusCode.BadRequest, approveResponse.StatusCode);
        var unchangedSettlement = await (await client.GetAsync($"/api/v1/cod/merchant-settlements/{settlement.Id}")).Content.ReadFromJsonAsync<MerchantSettlementResponse>();
        Assert.Equal(settlement.TotalPayable, unchangedSettlement!.TotalPayable);
    }

    // ---- Task 11: merchant-facing GET /api/v1/cod/settlements, GET /api/v1/cod/settlements/{id} ----

    [Fact]
    public async Task MerchantSettlements_MerchantJwt_SeesOwnSettlement()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "merchant-own");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(staffClient);
        var pickupRiderId = await CreateRiderAsync(staffClient, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(staffClient, destinationBranchId, RiderRoleType.DeliveryRider);
        var (merchantId, ownerEmail, password) = await CreateMerchantWithLoginAsync(staffClient, "own");

        var settlement = await SeedMerchantSettlementAsync(staffClient, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);

        var merchantClient = await LoginMerchantAsync(ownerEmail, password);

        var listResponse = await merchantClient.GetAsync("/api/v1/cod/settlements");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<List<MerchantSettlementResponse>>();
        Assert.Contains(list!, s => s.Id == settlement.Id);

        var getResponse = await merchantClient.GetAsync($"/api/v1/cod/settlements/{settlement.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<MerchantSettlementResponse>();
        Assert.Equal(settlement.Id, fetched!.Id);
        Assert.Equal(merchantId, fetched.MerchantId);
    }

    [Fact]
    public async Task SettlementExport_Merchant_ReturnsCsvWithHeaderAndTotal()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "export-own");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(staffClient);
        var pickupRiderId = await CreateRiderAsync(staffClient, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(staffClient, destinationBranchId, RiderRoleType.DeliveryRider);
        var (merchantId, ownerEmail, password) = await CreateMerchantWithLoginAsync(staffClient, "export-own");
        var settlement = await SeedMerchantSettlementAsync(staffClient, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var merchantClient = await LoginMerchantAsync(ownerEmail, password);

        var response = await merchantClient.GetAsync($"/api/v1/cod/settlements/{settlement.Id}/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/csv", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("settlement-", response.Content.Headers.ContentDisposition!.ToString());
        var lines = (await response.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("AWB,COD amount,Courier fee,Other deductions,Net payable", lines[0]);
        Assert.StartsWith("Total,", lines[^1]);
    }

    [Fact]
    public async Task SettlementExport_ForeignSettlement_Returns404()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "export-foreign");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(staffClient);
        var pickupRiderId = await CreateRiderAsync(staffClient, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(staffClient, destinationBranchId, RiderRoleType.DeliveryRider);
        var (merchantAId, _, _) = await CreateMerchantWithLoginAsync(staffClient, "export-foreign-a");
        var settlementA = await SeedMerchantSettlementAsync(staffClient, merchantAId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var (_, ownerEmailB, passwordB) = await CreateMerchantWithLoginAsync(staffClient, "export-foreign-b");
        var merchantBClient = await LoginMerchantAsync(ownerEmailB, passwordB);

        Assert.Equal(HttpStatusCode.NotFound, (await merchantBClient.GetAsync($"/api/v1/cod/settlements/{settlementA.Id}/export")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await merchantBClient.GetAsync($"/api/v1/cod/settlements/{Guid.NewGuid()}/export")).StatusCode);
    }

    [Fact]
    public async Task SettlementExport_StaffToken_Returns403()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "export-staff");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await staffClient.GetAsync($"/api/v1/cod/settlements/{Guid.NewGuid()}/export")).StatusCode);
    }

    [Fact]
    public async Task MerchantSettlements_MerchantJwt_NoSettlements_Returns200Empty()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "merchant-empty");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (merchantId, ownerEmail, password) = await CreateMerchantWithLoginAsync(staffClient, "empty");
        _ = merchantId;

        var merchantClient = await LoginMerchantAsync(ownerEmail, password);

        var listResponse = await merchantClient.GetAsync("/api/v1/cod/settlements");

        // Must be 200 + empty, not an error — a merchant with no settlements yet is a normal,
        // expected state, not a failure.
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<List<MerchantSettlementResponse>>();
        Assert.Empty(list!);
    }

    [Fact]
    public async Task MerchantSettlements_MerchantJwt_ForeignSettlementId_Returns404NotForbidden()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "merchant-foreign");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(staffClient);
        var pickupRiderId = await CreateRiderAsync(staffClient, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(staffClient, destinationBranchId, RiderRoleType.DeliveryRider);

        var (merchantAId, ownerEmailA, passwordA) = await CreateMerchantWithLoginAsync(staffClient, "foreign-a");
        var settlementA = await SeedMerchantSettlementAsync(staffClient, merchantAId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);

        var (_, ownerEmailB, passwordB) = await CreateMerchantWithLoginAsync(staffClient, "foreign-b");
        var merchantBClient = await LoginMerchantAsync(ownerEmailB, passwordB);

        var getResponse = await merchantBClient.GetAsync($"/api/v1/cod/settlements/{settlementA.Id}");

        // 404, not 403: a 403 would confirm the id exists but belongs to someone else, leaking
        // its existence. CodService.GetMerchantSettlementByIdAsync returns null (not a
        // forbidden-style signal) for a settlement owned by a different merchant, same as it does
        // for a genuinely nonexistent id — the two cases must be indistinguishable to the caller.
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        // Cross-check: a truly nonexistent id produces the identical 404, proving the foreign-id
        // response above isn't coincidentally a 404 for some unrelated reason.
        var getMissingResponse = await merchantBClient.GetAsync($"/api/v1/cod/settlements/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, getMissingResponse.StatusCode);

        // Merchant B's list must not contain A's settlement either.
        var listResponse = await merchantBClient.GetAsync("/api/v1/cod/settlements");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<List<MerchantSettlementResponse>>();
        Assert.DoesNotContain(list!, s => s.Id == settlementA.Id);
    }

    [Fact]
    public async Task MerchantSettlements_ApiKeyCallerScopedForCodSettlementsRead_CanList()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "merchant-apikey");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(staffClient);
        var pickupRiderId = await CreateRiderAsync(staffClient, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(staffClient, destinationBranchId, RiderRoleType.DeliveryRider);
        var (merchantId, ownerEmail, password) = await CreateMerchantWithLoginAsync(staffClient, "apikey");

        var settlement = await SeedMerchantSettlementAsync(staffClient, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);

        var merchantClient = await LoginMerchantAsync(ownerEmail, password);
        var createKeyResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/api-keys",
            new CreateApiKeyRequest(new[] { MerchantPermissionCatalog.CodSettlementsRead }));
        createKeyResponse.EnsureSuccessStatusCode();
        var apiKey = await createKeyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        var apiKeyClient = _factory.CreateClient();
        apiKeyClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey!.PlaintextKey);

        var listResponse = await apiKeyClient.GetAsync("/api/v1/cod/settlements");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<List<MerchantSettlementResponse>>();
        Assert.Contains(list!, s => s.Id == settlement.Id);
    }

    // Regression guard: proves this task's additive route group left the existing staff-only
    // /api/v1/cod/merchant-settlements route (single-scheme .RequireAuthorization() +
    // .RequirePermission("cod.read")) untouched — a staff JWT can still call it exactly as before.
    [Fact]
    public async Task ExistingStaffMerchantSettlementsRoute_StillWorks_Unmodified()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "staff-regression");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(staffClient);
        var pickupRiderId = await CreateRiderAsync(staffClient, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(staffClient, destinationBranchId, RiderRoleType.DeliveryRider);
        var (merchantId, _, _) = await CreateMerchantWithLoginAsync(staffClient, "staff-regression");

        var settlement = await SeedMerchantSettlementAsync(staffClient, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);

        var listResponse = await staffClient.GetAsync("/api/v1/cod/merchant-settlements");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<List<MerchantSettlementResponse>>();
        Assert.Contains(list!, s => s.Id == settlement.Id);

        var getResponse = await staffClient.GetAsync($"/api/v1/cod/merchant-settlements/{settlement.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<MerchantSettlementResponse>();
        Assert.Equal(settlement.Id, fetched!.Id);
    }

    [Fact]
    public async Task GetCodSummary_ReturnsZeroedSummaryForNewOrg()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "summary");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/cod/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CodFinancialSummary>();
        Assert.Equal(0m, body!.Expected);
    }

    [Fact]
    public async Task GetCodSummary_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/cod/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- F15 R3a: rider-settlement variance, date filter, collections-by-settlement ----

    private static string UtcParam(DateTimeOffset value) => Uri.EscapeDataString(value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));

    [Fact]
    public async Task RiderSettlement_ShortDeposit_ReportsExpectedAndVariance_AndFiltersByDate()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-r3a-variance");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var shipment = await BookAndCompleteCodDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);

        var createResponse = await client.PostAsJsonAsync("/api/v1/cod/rider-settlements", new CreateRiderSettlementRequest(deliveryRiderId, destinationBranchId, 2400m));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<RiderCashSettlementResponse>();
        Assert.Equal(2500m, created!.ExpectedAmount);
        Assert.Equal(-100m, created.Variance);

        var inRange = await (await client.GetAsync($"/api/v1/cod/rider-settlements?from={UtcParam(DateTimeOffset.UtcNow.AddDays(-1))}&to={UtcParam(DateTimeOffset.UtcNow.AddDays(1))}"))
            .Content.ReadFromJsonAsync<PagedResult<RiderCashSettlementResponse>>();
        var listed = Assert.Single(inRange!.Items, r => r.Id == created.Id);
        Assert.Equal(-100m, listed.Variance);

        var future = await (await client.GetAsync($"/api/v1/cod/rider-settlements?from={UtcParam(DateTimeOffset.UtcNow.AddDays(1))}"))
            .Content.ReadFromJsonAsync<PagedResult<RiderCashSettlementResponse>>();
        Assert.DoesNotContain(future!.Items, r => r.Id == created.Id);

        var inverted = await client.GetAsync($"/api/v1/cod/rider-settlements?from={UtcParam(DateTimeOffset.UtcNow)}&to={UtcParam(DateTimeOffset.UtcNow.AddDays(-1))}");
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);

        var bySettlement = await (await client.GetAsync($"/api/v1/cod/collections?riderCashSettlementId={created.Id}"))
            .Content.ReadFromJsonAsync<PagedResult<CodCollectionResponse>>();
        var parcel = Assert.Single(bySettlement!.Items);
        Assert.Equal(shipment.Id, parcel.ShipmentId);
        Assert.Equal(shipment.Awb, parcel.Awb);
    }

    [Theory]
    [InlineData("collections")]
    [InlineData("branch-settlements")]
    [InlineData("merchant-settlements")]
    [InlineData("adjustments")]
    public async Task ListTabs_InvertedDateRange_Returns400_AndValidRangeReturns200(string path)
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, $"cod-range-{path}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var inverted = await client.GetAsync($"/api/v1/cod/{path}?from={UtcParam(DateTimeOffset.UtcNow)}&to={UtcParam(DateTimeOffset.UtcNow.AddDays(-1))}");
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);

        var valid = await client.GetAsync($"/api/v1/cod/{path}?from={UtcParam(DateTimeOffset.UtcNow.AddDays(-7))}&to={UtcParam(DateTimeOffset.UtcNow)}");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    private record FlowStage(string Key, decimal Amount, int Count);
    private record FlowBody(List<FlowStage> Stages);
    private record RiderCashItem(Guid RiderId, Guid BranchId, int ParcelCount, decimal Amount, DateTimeOffset OldestCollectedAt, DateTimeOffset LatestCollectedAt);
    private record RiderCashBody(List<RiderCashItem> Items);

    [Fact]
    public async Task FlowAndRiderCash_TrackCashFromDoorstepToDeposit()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-r3a-flow");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        await BookAndCompleteCodDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);

        var flowResponse = await client.GetAsync("/api/v1/cod/flow");
        Assert.Equal(HttpStatusCode.OK, flowResponse.StatusCode);
        var flow = await flowResponse.Content.ReadFromJsonAsync<FlowBody>();
        Assert.Equal(new[] { "expected", "collected", "riderDeposited", "branchReconciled", "financeVerified", "settled" }, flow!.Stages.Select(s => s.Key));
        Assert.Equal(2500m, flow.Stages[1].Amount);
        Assert.Equal(0m, flow.Stages[2].Amount);

        var holding = await (await client.GetAsync("/api/v1/cod/rider-cash")).Content.ReadFromJsonAsync<RiderCashBody>();
        var row = Assert.Single(holding!.Items);
        Assert.Equal(deliveryRiderId, row.RiderId);
        Assert.Equal(destinationBranchId, row.BranchId);
        Assert.Equal(2500m, row.Amount);

        (await client.PostAsJsonAsync("/api/v1/cod/rider-settlements", new CreateRiderSettlementRequest(deliveryRiderId, destinationBranchId, 2500m))).EnsureSuccessStatusCode();

        var afterDeposit = await (await client.GetAsync("/api/v1/cod/rider-cash")).Content.ReadFromJsonAsync<RiderCashBody>();
        Assert.Empty(afterDeposit!.Items);
        var flowAfter = await (await client.GetAsync("/api/v1/cod/flow")).Content.ReadFromJsonAsync<FlowBody>();
        Assert.Equal(2500m, flowAfter!.Stages[2].Amount);

        var inverted = await client.GetAsync($"/api/v1/cod/flow?from={UtcParam(DateTimeOffset.UtcNow)}&to={UtcParam(DateTimeOffset.UtcNow.AddDays(-1))}");
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
    }

    [Fact]
    public async Task FlowAndRiderCash_MerchantCaller_Returns403()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "cod-r3a-merchant");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (_, ownerEmail, password) = await CreateMerchantWithLoginAsync(staffClient, "r3a");
        var merchantClient = await LoginMerchantAsync(ownerEmail, password);

        Assert.Equal(HttpStatusCode.Forbidden, (await merchantClient.GetAsync("/api/v1/cod/flow")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await merchantClient.GetAsync("/api/v1/cod/rider-cash")).StatusCode);
    }

    // ---- F15 R5b-1: merchant portal summary / awaiting / per-shipment COD status ----

    private async Task<(HttpClient Staff, Guid OriginBranchId, Guid DestinationBranchId, Guid ServiceTypeId, Guid PickupRiderId, Guid DeliveryRiderId)> SeedPortalOrgAsync(string label)
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, label);
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(staffClient);
        var pickupRiderId = await CreateRiderAsync(staffClient, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(staffClient, destinationBranchId, RiderRoleType.DeliveryRider);
        return (staffClient, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId);
    }

    [Fact]
    public async Task MerchantSummary_MerchantJwt_SeesOwnCollectedAmount()
    {
        var (staff, origin, destination, serviceTypeId, pickupRiderId, deliveryRiderId) = await SeedPortalOrgAsync("portal-summary");
        var (merchantId, ownerEmail, password) = await CreateMerchantWithLoginAsync(staff, "portal-summary");
        await BookAndCompleteCodDeliveryAsync(staff, merchantId, origin, destination, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var merchantClient = await LoginMerchantAsync(ownerEmail, password);

        var response = await merchantClient.GetAsync("/api/v1/cod/merchant-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<MerchantCodSummaryResponse>();
        Assert.Equal(2500m, summary!.CollectedNotSettled.Amount);
        Assert.Equal(1, summary.CollectedNotSettled.Count);
    }

    [Fact]
    public async Task MerchantSummary_SecondMerchantSameOrg_SeesZero()
    {
        var (staff, origin, destination, serviceTypeId, pickupRiderId, deliveryRiderId) = await SeedPortalOrgAsync("portal-summary-b");
        var (merchantAId, _, _) = await CreateMerchantWithLoginAsync(staff, "portal-a");
        var (_, ownerEmailB, passwordB) = await CreateMerchantWithLoginAsync(staff, "portal-b");
        await BookAndCompleteCodDeliveryAsync(staff, merchantAId, origin, destination, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var merchantBClient = await LoginMerchantAsync(ownerEmailB, passwordB);

        var summary = await (await merchantBClient.GetAsync("/api/v1/cod/merchant-summary")).Content.ReadFromJsonAsync<MerchantCodSummaryResponse>();
        var awaiting = await (await merchantBClient.GetAsync("/api/v1/cod/merchant-awaiting")).Content.ReadFromJsonAsync<PagedResult<MerchantAwaitingCollectionResponse>>();

        Assert.Equal(0m, summary!.CollectedNotSettled.Amount);
        Assert.Equal(0, summary.CollectedNotSettled.Count);
        Assert.Empty(awaiting!.Items);
    }

    [Fact]
    public async Task MerchantSummary_StaffToken_Returns403()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "portal-staff");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await staffClient.GetAsync("/api/v1/cod/merchant-summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staffClient.GetAsync("/api/v1/cod/merchant-awaiting")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staffClient.GetAsync($"/api/v1/cod/merchant-shipments/{Guid.NewGuid()}/cod-status")).StatusCode);
    }

    [Fact]
    public async Task MerchantAwaiting_ListsOwnCollection_WithAwb()
    {
        var (staff, origin, destination, serviceTypeId, pickupRiderId, deliveryRiderId) = await SeedPortalOrgAsync("portal-awaiting");
        var (merchantId, ownerEmail, password) = await CreateMerchantWithLoginAsync(staff, "portal-awaiting");
        var shipment = await BookAndCompleteCodDeliveryAsync(staff, merchantId, origin, destination, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var merchantClient = await LoginMerchantAsync(ownerEmail, password);

        var response = await merchantClient.GetAsync("/api/v1/cod/merchant-awaiting");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<MerchantAwaitingCollectionResponse>>();
        var row = Assert.Single(page!.Items);
        Assert.Equal(shipment.Id, row.ShipmentId);
        Assert.Equal(shipment.Awb, row.Awb);
        Assert.Equal(2500m, row.CollectedAmount);
        Assert.Equal(row.CollectedAmount - row.CourierFee, row.Net);
    }

    [Fact]
    public async Task MerchantCodStatus_ForeignShipment_Returns404()
    {
        var (staff, origin, destination, serviceTypeId, pickupRiderId, deliveryRiderId) = await SeedPortalOrgAsync("portal-foreign");
        var (merchantAId, _, _) = await CreateMerchantWithLoginAsync(staff, "portal-foreign-a");
        var (_, ownerEmailB, passwordB) = await CreateMerchantWithLoginAsync(staff, "portal-foreign-b");
        var shipmentA = await BookAndCompleteCodDeliveryAsync(staff, merchantAId, origin, destination, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var merchantBClient = await LoginMerchantAsync(ownerEmailB, passwordB);

        var foreign = await merchantBClient.GetAsync($"/api/v1/cod/merchant-shipments/{shipmentA.Id}/cod-status");
        var missing = await merchantBClient.GetAsync($"/api/v1/cod/merchant-shipments/{Guid.NewGuid()}/cod-status");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task MerchantCodStatus_AfterSettlement_ReturnsInSettlement()
    {
        var (staff, origin, destination, serviceTypeId, pickupRiderId, deliveryRiderId) = await SeedPortalOrgAsync("portal-status");
        var (merchantId, ownerEmail, password) = await CreateMerchantWithLoginAsync(staff, "portal-status");
        var settlement = await SeedMerchantSettlementAsync(staff, merchantId, origin, destination, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var shipmentId = settlement.Items.Single().ShipmentId;
        var merchantClient = await LoginMerchantAsync(ownerEmail, password);

        var response = await merchantClient.GetAsync($"/api/v1/cod/merchant-shipments/{shipmentId}/cod-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<MerchantShipmentCodStatusResponse>();
        Assert.Equal(MerchantCodState.InSettlement, status!.State);
        Assert.Equal(settlement.Id, status.SettlementId);
    }
}
