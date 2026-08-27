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

// Task 14: proves Phase 12's exit criteria almost verbatim — "an external client can create a
// shipment, poll its status, and see a settlement purely through the documented Merchant API."
// No production code is touched by this task; this is a proving test that stitches together the
// helper conventions from ShipmentsEndpointsTests.cs (branch/zone/rate-card seeding),
// MerchantAuthAndStaffEndpointsTests.cs (merchant registration/login), ApiKeyEndpointsTests.cs
// (Task 4, API key creation), CodEndpointsTests.cs (staff-side settlement-creation flow), and
// ApiKeyAuthenticationTests.cs (Task 5, X-Api-Key header usage).
public class MerchantApiExitCriteriaEndToEndTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public MerchantApiExitCriteriaEndToEndTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"Exit Criteria Org {suffix}", $"exit-criteria-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
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
        var originZoneResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Origin-{Guid.NewGuid():N}", KathmanduWard1Areas()));
        var originZone = await originZoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        var destZoneResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Dest-{Guid.NewGuid():N}", KathmanduWard1Areas()));
        var destZone = await destZoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();

        var originBranchResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"OZ-{Guid.NewGuid():N}"[..10], originZone!.Id));
        var originBranch = await originBranchResponse.Content.ReadFromJsonAsync<BranchResponse>();
        var destBranchResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"DZ-{Guid.NewGuid():N}"[..10], destZone!.Id));
        var destBranch = await destBranchResponse.Content.ReadFromJsonAsync<BranchResponse>();

        var serviceTypesResponse = await client.GetAsync("/api/v1/service-types");
        var serviceTypes = await serviceTypesResponse.Content.ReadFromJsonAsync<List<ServiceTypeResponse>>();
        var standard = serviceTypes!.Single(s => s.Code == ServiceTypeCode.Standard);

        return (originBranch!.Id, destBranch!.Id, standard.Id);
    }

    private async Task SeedRateCardAsync(HttpClient client, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, decimal baseRate)
    {
        var originBranch = (await (await client.GetAsync($"/api/v1/branches/{originBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;
        var destBranch = (await (await client.GetAsync($"/api/v1/branches/{destinationBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;
        var weightSlabs = await (await client.GetAsync("/api/v1/weight-slabs")).Content.ReadFromJsonAsync<List<WeightSlabResponse>>();
        var slab = weightSlabs!.Single(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m);

        var rateCardResponse = await client.PostAsJsonAsync("/api/v1/rate-cards", new CreateRateCardRequest($"Card-{Guid.NewGuid():N}", DateTimeOffset.UtcNow));
        rateCardResponse.EnsureSuccessStatusCode();
        var rateCard = await rateCardResponse.Content.ReadFromJsonAsync<RateCardResponse>();
        var zoneRateResponse = await client.PostAsJsonAsync($"/api/v1/rate-cards/{rateCard!.Id}/zone-rates",
            new CreateZoneRateRequest(originBranch.DeliveryZoneId!.Value, destBranch.DeliveryZoneId!.Value, serviceTypeId, slab.Id, baseRate));
        zoneRateResponse.EnsureSuccessStatusCode();
    }

    private async Task<Guid> CreateRiderAsync(HttpClient client, Guid branchId, RiderRoleType roleType)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-{suffix}@example.com", "P@ssw0rd123!", "Test Rider", null, $"LIC-{suffix[..10]}", branchId, roleType));
        response.EnsureSuccessStatusCode();
        var rider = await response.Content.ReadFromJsonAsync<RiderResponse>();
        return rider!.Id;
    }

    // Mirrors CodEndpointsTests.cs's CreateMerchantWithLoginAsync — hands back the merchant's id
    // plus login credentials so the test can log in as the Owner in Step 2.
    private async Task<(Guid MerchantId, string OwnerEmail, string Password)> CreateMerchantWithLoginAsync(HttpClient staffClient, string label)
    {
        var ownerEmail = $"exit-owner-{label}-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            $"Exit Criteria Shipper {label} Pvt. Ltd.", $"Exit Criteria Shipper {label}", "01-4444444", $"exit-shipper-{label}@example.com",
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

    // API-key/merchant-track callers are gated by IdempotencyMiddleware (Task 7) on
    // POST /api/v1/shipments — every such POST must carry an Idempotency-Key header or the
    // middleware short-circuits with 400 before the request reaches the handler under test.
    private static async Task<HttpResponseMessage> PostIdempotentAsync<T>(HttpClient client, string url, T body, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task ExternalClient_CreatesShipmentPollsStatusAndSeesSettlement_PurelyThroughMerchantApi()
    {
        // ---- Step 1: staff JWT, unavoidable bootstrapping ----
        // This is infrastructure a real courier ops team configures once (org, branches, delivery
        // zones, rate card, riders, merchant onboarding), not part of the "external client" claim
        // this test proves. staffClient is never touched again after Step 5.
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "exit-criteria");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var pickupRiderId = await CreateRiderAsync(staffClient, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(staffClient, destinationBranchId, RiderRoleType.DeliveryRider);
        var (merchantId, ownerEmail, password) = await CreateMerchantWithLoginAsync(staffClient, "exit-criteria");

        // ---- Step 2: merchant JWT, one-time, only to mint an API key ----
        var ownerClient = await LoginMerchantAsync(ownerEmail, password);
        var createKeyResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys", new CreateApiKeyRequest());
        createKeyResponse.EnsureSuccessStatusCode();
        var apiKey = await createKeyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        // apiKeyClient is a brand-new HttpClient (never shared with staffClient/ownerClient above)
        // and DefaultRequestHeaders.Authorization is never set on it anywhere below — that
        // omission is what structurally proves steps 3, 4 and 6 use no JWT, rather than an
        // assertion inspecting outgoing request headers after the fact.
        var apiKeyClient = _factory.CreateClient();
        apiKeyClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey!.PlaintextKey);

        // ---- Step 3: Create (API key only) ----
        const decimal codAmount = 2500m;
        var createShipmentRequest = new CreateShipmentRequest(
            null, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, codAmount, false,
            "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
            "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
            new List<CreateShipmentItemRequest> { new("Item A", 1, 500m) });
        var createResponse = await PostIdempotentAsync(apiKeyClient, "/api/v1/shipments", createShipmentRequest, "exit-criteria-create-1");
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        // MerchantId is forced server-side from the API key's owning merchant, never trusted from
        // the (null) request body — proves the created shipment really belongs to this merchant.
        Assert.Equal(merchantId, created!.MerchantId);
        Assert.Equal(codAmount, created.CodAmount);
        Assert.Equal(ShipmentStatus.Created, created.CurrentStatus);

        // ---- Step 4: Poll (API key only) ----
        var pollResponse = await apiKeyClient.GetAsync($"/api/v1/shipments/{created.Awb}");
        Assert.Equal(HttpStatusCode.OK, pollResponse.StatusCode);
        var polled = await pollResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(created.Id, polled!.Id);
        Assert.Equal(created.Awb, polled.Awb);
        Assert.Equal(merchantId, polled.MerchantId);
        Assert.Equal(codAmount, polled.CodAmount);

        // ---- Step 5: staff JWT, unavoidable — settlement creation is staff-only this phase ----
        // Drives THIS SAME shipment (the one created via the API key above, identified by its Id/
        // Awb) through pickup, sort, hub transfer and COD delivery, then rider -> branch ->
        // finance -> merchant settlement, reusing CodEndpointsTests.cs's existing staff flow shape
        // (BookAndCompleteCodDeliveryAsync / SeedMerchantSettlementAsync) minus the initial booking
        // call, since the shipment to settle already exists. None of this staff-driven machinery is
        // part of the "external client" claim being proven — it produces the one real
        // MerchantSettlement row that Step 6 reads back purely through the Merchant API.
        await staffClient.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, created.Id, DeliveryTaskType.Pickup));
        await staffClient.PostAsJsonAsync($"/api/v1/riders/{pickupRiderId}/scan/pickup", new RiderScanRequest(created.Awb));
        await staffClient.PostAsJsonAsync($"/api/v1/shipments/{created.Id}/receive-origin", new { });

        var manifest = await (await staffClient.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await staffClient.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(created.Awb));
        await staffClient.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await staffClient.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await staffClient.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(created.Awb));

        await staffClient.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, created.Id, DeliveryTaskType.Delivery));
        var completeResponse = await staffClient.PostAsJsonAsync($"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete",
            new CompleteDeliveryRequest(created.Id, "Recipient One", true, null, null, null, null, codAmount));
        completeResponse.EnsureSuccessStatusCode();
        var delivered = await (await staffClient.GetAsync($"/api/v1/shipments/{created.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Delivered, delivered!.CurrentStatus);

        var riderSettlement = await (await staffClient.PostAsJsonAsync("/api/v1/cod/rider-settlements",
            new CreateRiderSettlementRequest(deliveryRiderId, destinationBranchId, codAmount))).Content.ReadFromJsonAsync<RiderCashSettlementResponse>();
        var branchSettlement = await (await staffClient.PostAsJsonAsync("/api/v1/cod/branch-settlements",
            new CreateBranchSettlementRequest(destinationBranchId, new List<Guid> { riderSettlement!.Id }, codAmount))).Content.ReadFromJsonAsync<BranchCashSettlementResponse>();
        var financeVerifyResponse = await staffClient.PostAsJsonAsync("/api/v1/cod/finance-verifications", new CreateFinanceVerificationRequest(branchSettlement!.Id, codAmount));
        financeVerifyResponse.EnsureSuccessStatusCode();

        var merchantSettlementResponse = await staffClient.PostAsJsonAsync("/api/v1/cod/merchant-settlements",
            new CreateMerchantSettlementRequest(merchantId, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)));
        merchantSettlementResponse.EnsureSuccessStatusCode();
        var settlement = await merchantSettlementResponse.Content.ReadFromJsonAsync<MerchantSettlementResponse>();
        Assert.Equal(merchantId, settlement!.MerchantId);
        var settlementItem = Assert.Single(settlement.Items);
        // Proves the settlement produced here is for THIS shipment — the one created purely
        // through the Merchant API in Step 3 — not merely some other settlement for the merchant.
        Assert.Equal(created.Id, settlementItem.ShipmentId);
        Assert.Equal(codAmount, settlementItem.CODAmount);

        // ---- Step 6: Read the settlement (API key only, the same apiKeyClient used in steps 3
        // and 4 — still no Authorization header has ever been set on it) ----
        var settlementsListResponse = await apiKeyClient.GetAsync("/api/v1/cod/settlements");
        Assert.Equal(HttpStatusCode.OK, settlementsListResponse.StatusCode);
        var settlementsList = await settlementsListResponse.Content.ReadFromJsonAsync<List<MerchantSettlementResponse>>();
        // The settlement read back must be the EXACT one created in Step 5 — same id, same total
        // payable, and its line item must reference the same shipment created in Step 3 — not
        // merely "a settlement exists" for the merchant.
        var readBack = Assert.Single(settlementsList!, s => s.Id == settlement.Id);
        Assert.Equal(settlement.TotalPayable, readBack.TotalPayable);
        Assert.Equal(MerchantSettlementStatus.Created, readBack.Status);
        var readBackItem = Assert.Single(readBack.Items);
        Assert.Equal(created.Id, readBackItem.ShipmentId);
        Assert.Equal(codAmount, readBackItem.CODAmount);

        // ---- Step 7 ----
        // No Authorization: Bearer header was ever used for steps 3, 4 or 6: apiKeyClient was
        // constructed fresh in Step 2 with only an X-Api-Key header, and this test never assigns
        // apiKeyClient.DefaultRequestHeaders.Authorization (nor sends a per-request Authorization
        // header on it) anywhere above. That is a structural property of the code, not something
        // proven by inspecting captured request headers after the fact.
    }
}
