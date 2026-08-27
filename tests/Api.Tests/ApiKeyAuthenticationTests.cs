using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Shipments.Application;
using Courier.Webhooks.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

// Covers Task 5: the "ApiKey" authentication scheme (X-Api-Key or Authorization: Bearer carrying
// a plaintext API key instead of a JWT) and its wiring into Shipments (multi-scheme
// .RequireAuthorization()) and Webhooks (RequireMerchantPermissionAnyCredential). Mirrors
// ApiKeyEndpointsTests.cs's and ShipmentsEndpointsTests.cs's helper conventions.
public class ApiKeyAuthenticationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public ApiKeyAuthenticationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> RegisterOrgAndGetStaffTokenAsync(HttpClient client, string label)
    {
        // Unlike the sibling test files' `[..12]`-on-the-whole-string pattern (safe there only
        // because their labels are all short), this file's labels are descriptive and some are
        // combined with a "-staff" suffix (SeedMerchantWithRouteAsync below) — truncating the
        // combined string to 12 chars would silently drop some or all of the GUID's randomness for
        // longer labels, producing a deterministic slug that collides against the persistent
        // shared Postgres test database on a second run. Keeping a fixed-length 12-hex-char GUID
        // segment guarantees real uniqueness regardless of label length.
        var suffix = $"{label}-{Guid.NewGuid().ToString("N")[..12]}";
        var request = new RegisterOrganizationRequest(
            $"ApiKeyAuth Org {suffix}", $"apikeyauth-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        return auth.AccessToken;
    }

    private async Task<(string OwnerEmail, string Password)> CreateMerchantAsync(HttpClient staffClient, string label)
    {
        var ownerEmail = $"owner-{label}-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            "Acme Traders Pvt. Ltd.", "Acme Traders", "01-4444444", "contact@acme.example",
            ownerEmail, "P@ssw0rd123!", "Merchant Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu");
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", request);
        response.EnsureSuccessStatusCode();
        return (ownerEmail, "P@ssw0rd123!");
    }

    private async Task<HttpClient> CreateOwnerClientAsync(string label)
    {
        var staffClient = _factory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, label);
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (ownerEmail, ownerPassword) = await CreateMerchantAsync(staffClient, label);

        var ownerClient = _factory.CreateClient();
        var ownerLogin = await ownerClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, ownerPassword));
        ownerLogin.EnsureSuccessStatusCode();
        var ownerAuth = await ownerLogin.Content.ReadFromJsonAsync<MerchantAuthResult>();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAuth!.AccessToken);
        return ownerClient;
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

    private static CreateShipmentRequest ValidShipmentRequest(Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId) => new(
        null, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
        "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
        "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 2, 500m) });

    // Sets up a full merchant + routable branches + rate card, logs in the owner, and hands back
    // both the owner's JWT-authenticated HttpClient and the seeded route so callers can create an
    // API key and immediately exercise it against POST /api/v1/shipments.
    private async Task<(HttpClient OwnerClient, Guid OriginBranchId, Guid DestinationBranchId, Guid ServiceTypeId)> SeedMerchantWithRouteAsync(string label)
    {
        var ownerClient = await CreateOwnerClientAsync(label);

        // Branches/rate cards are org-scoped, created by staff — reuse the same staff org the
        // merchant belongs to by registering a fresh staff client under a fresh org and merchant
        // instead: simpler, and route setup only needs to be org-consistent with itself, not with
        // the merchant's org, since ShipmentService resolves the merchant's OrganizationId from
        // the merchant record itself. To keep it in the SAME org as the merchant (required for the
        // route to be visible to that merchant's shipment creation), seed branches/rate cards with
        // a staff client belonging to the merchant's own organization.
        var staffClient = _factory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, $"{label}-staff");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);

        return (ownerClient, originBranchId, destinationBranchId, serviceTypeId);
    }

    [Fact]
    public async Task ApiKeyViaXApiKeyHeader_CreateShipment_Returns201()
    {
        var staffClient = _factory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, "xheader");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var (ownerEmail, ownerPassword) = await CreateMerchantAsync(staffClient, "xheader");

        var ownerClient = _factory.CreateClient();
        var ownerLogin = await ownerClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, ownerPassword));
        ownerLogin.EnsureSuccessStatusCode();
        var ownerAuth = await ownerLogin.Content.ReadFromJsonAsync<MerchantAuthResult>();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAuth!.AccessToken);

        var createKeyResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys", new CreateApiKeyRequest());
        createKeyResponse.EnsureSuccessStatusCode();
        var apiKey = await createKeyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        var apiKeyClient = _factory.CreateClient();
        apiKeyClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey!.PlaintextKey);

        var shipmentResponse = await apiKeyClient.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(originBranchId, destinationBranchId, serviceTypeId));

        Assert.Equal(HttpStatusCode.Created, shipmentResponse.StatusCode);
        var shipment = await shipmentResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ownerAuth.MerchantId, shipment!.MerchantId);
    }

    [Fact]
    public async Task ApiKeyViaAuthorizationBearerHeader_CreateShipment_Returns201()
    {
        var staffClient = _factory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, "abearer");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var (ownerEmail, ownerPassword) = await CreateMerchantAsync(staffClient, "abearer");

        var ownerClient = _factory.CreateClient();
        var ownerLogin = await ownerClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, ownerPassword));
        ownerLogin.EnsureSuccessStatusCode();
        var ownerAuth = await ownerLogin.Content.ReadFromJsonAsync<MerchantAuthResult>();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAuth!.AccessToken);

        var createKeyResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys", new CreateApiKeyRequest());
        createKeyResponse.EnsureSuccessStatusCode();
        var apiKey = await createKeyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        // Same plaintext key, this time presented as "Authorization: Bearer {plaintextKey}"
        // instead of X-Api-Key — proves both header forms reach ApiKeyAuthenticationHandler and
        // authenticate identically (the JWT-shaped "Bearer" scheme independently fails on this
        // same header since the plaintext key is not a valid JWT).
        var apiKeyClient = _factory.CreateClient();
        apiKeyClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey!.PlaintextKey);

        var shipmentResponse = await apiKeyClient.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(originBranchId, destinationBranchId, serviceTypeId));

        Assert.Equal(HttpStatusCode.Created, shipmentResponse.StatusCode);
    }

    [Fact]
    public async Task RevokedApiKey_CreateShipment_Returns401()
    {
        var (ownerClient, originBranchId, destinationBranchId, serviceTypeId) = await SeedMerchantWithRouteAsync("revoked");

        var createKeyResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys", new CreateApiKeyRequest());
        createKeyResponse.EnsureSuccessStatusCode();
        var apiKey = await createKeyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        var revokeResponse = await ownerClient.DeleteAsync($"/api/v1/merchant/api-keys/{apiKey!.Id}");
        revokeResponse.EnsureSuccessStatusCode();

        var apiKeyClient = _factory.CreateClient();
        apiKeyClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey.PlaintextKey);

        var shipmentResponse = await apiKeyClient.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(originBranchId, destinationBranchId, serviceTypeId));

        Assert.Equal(HttpStatusCode.Unauthorized, shipmentResponse.StatusCode);
    }

    [Fact]
    public async Task GarbageApiKey_CreateShipment_Returns401()
    {
        var (_, originBranchId, destinationBranchId, serviceTypeId) = await SeedMerchantWithRouteAsync("garbage");

        var apiKeyClient = _factory.CreateClient();
        apiKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "not-a-real-key");

        var shipmentResponse = await apiKeyClient.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(originBranchId, destinationBranchId, serviceTypeId));

        Assert.Equal(HttpStatusCode.Unauthorized, shipmentResponse.StatusCode);
    }

    [Fact]
    public async Task ValidPrefixWrongSecret_CreateShipment_Returns401()
    {
        var (ownerClient, originBranchId, destinationBranchId, serviceTypeId) = await SeedMerchantWithRouteAsync("wrongsecr");

        var createKeyResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys", new CreateApiKeyRequest());
        createKeyResponse.EnsureSuccessStatusCode();
        var apiKey = await createKeyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        var apiKeyClient = _factory.CreateClient();
        apiKeyClient.DefaultRequestHeaders.Add("X-Api-Key", $"{apiKey!.Prefix}.completely-wrong-secret-value");

        var shipmentResponse = await apiKeyClient.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(originBranchId, destinationBranchId, serviceTypeId));

        Assert.Equal(HttpStatusCode.Unauthorized, shipmentResponse.StatusCode);
    }

    [Fact]
    public async Task ApiKeyWithoutShipmentCreateScope_CreateShipment_Returns403()
    {
        var (ownerClient, originBranchId, destinationBranchId, serviceTypeId) = await SeedMerchantWithRouteAsync("noscope");

        var createKeyResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys",
            new CreateApiKeyRequest(new[] { MerchantPermissionCatalog.ShipmentRead }));
        createKeyResponse.EnsureSuccessStatusCode();
        var apiKey = await createKeyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        var apiKeyClient = _factory.CreateClient();
        apiKeyClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey!.PlaintextKey);

        var shipmentResponse = await apiKeyClient.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(originBranchId, destinationBranchId, serviceTypeId));

        Assert.Equal(HttpStatusCode.Forbidden, shipmentResponse.StatusCode);
    }

    [Fact]
    public async Task ApiKeyScopedForWebhooksManage_CanListWebhooks_ButNotWithoutScope()
    {
        var ownerClient = await CreateOwnerClientAsync("webhookscope");

        var scopedKeyResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys",
            new CreateApiKeyRequest(new[] { MerchantPermissionCatalog.WebhooksManage }));
        scopedKeyResponse.EnsureSuccessStatusCode();
        var scopedKey = await scopedKeyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Add("X-Api-Key", scopedKey!.PlaintextKey);

        var listResponse = await scopedClient.GetAsync("/api/v1/webhooks");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var unscopedKeyResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys",
            new CreateApiKeyRequest(new[] { MerchantPermissionCatalog.ShipmentRead }));
        unscopedKeyResponse.EnsureSuccessStatusCode();
        var unscopedKey = await unscopedKeyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        var unscopedClient = _factory.CreateClient();
        unscopedClient.DefaultRequestHeaders.Add("X-Api-Key", unscopedKey!.PlaintextKey);

        var forbiddenResponse = await unscopedClient.GetAsync("/api/v1/webhooks");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    // Security carve-out proof (Global Constraints / Task 5 brief): RequireMerchantPermission
    // (used by ApiKeyEndpoints, unlike RequireMerchantPermissionAnyCredential used by Webhooks and
    // Shipments' manual dual-auth branch) accepts only the "Bearer" scheme — an API key must never
    // be able to create/list/revoke another API key, even one scoped with ApiKeysManage.
    [Fact]
    public async Task ApiKeyWithApiKeysManageScope_CannotCreateAnotherApiKey_Returns401()
    {
        var ownerClient = await CreateOwnerClientAsync("selfescalation");

        var keyResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys",
            new CreateApiKeyRequest(new[] { MerchantPermissionCatalog.ApiKeysManage }));
        keyResponse.EnsureSuccessStatusCode();
        var apiKey = await keyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        var apiKeyClient = _factory.CreateClient();
        apiKeyClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey!.PlaintextKey);

        var selfEscalationAttempt = await apiKeyClient.PostAsJsonAsync("/api/v1/merchant/api-keys", new CreateApiKeyRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, selfEscalationAttempt.StatusCode);
    }
}
