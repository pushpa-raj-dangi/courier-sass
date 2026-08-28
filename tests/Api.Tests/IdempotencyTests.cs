using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

// Covers Task 7: IdempotencyMiddleware's enforcement of Idempotency-Key on merchant-track write
// routes (POST /api/v1/shipments, POST /api/v1/shipments/{id}/cancel, POST /api/v1/pickups).
// Mirrors ApiKeyAuthenticationTests.cs's and ShipmentsEndpointsTests.cs's helper conventions.
// The middleware self-selects on caller kind (GetMerchantId() is not null) — staff JWT callers on
// these same routes must remain completely untouched, which is why every existing
// ShipmentsEndpointsTests case (all staff-JWT-driven, none sending Idempotency-Key) must still
// pass unmodified alongside this file.
public class IdempotencyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public IdempotencyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> RegisterOrgAndGetStaffTokenAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid().ToString("N")[..12]}";
        var request = new RegisterOrganizationRequest(
            $"Idem Org {suffix}", $"idem-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        return auth.AccessToken;
    }

    private async Task<(Guid MerchantId, string OwnerEmail, string Password)> CreateMerchantAsync(HttpClient staffClient, string label)
    {
        var ownerEmail = $"owner-{label}-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            "Acme Traders Pvt. Ltd.", "Acme Traders", "01-4444444", "contact@acme.example",
            ownerEmail, "P@ssw0rd123!", "Merchant Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu");
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", request);
        response.EnsureSuccessStatusCode();
        var merchant = await response.Content.ReadFromJsonAsync<MerchantResponse>();
        return (merchant!.Id, ownerEmail, "P@ssw0rd123!");
    }

    // Scoped host with a small ApiKeyRequestsPerMinute limit so a test can trip
    // ApiKeyRateLimitFilter in a handful of requests instead of the real default of 60.
    // Mirrors RateLimitingTests.cs's CreateFactoryWithLowLimit helper.
    private WebApplicationFactory<Program> CreateFactoryWithLowApiKeyRateLimit(int limit) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:ApiKeyRequestsPerMinute"] = limit.ToString()
            })));

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

    // MerchantId is deliberately Guid.Empty (or null for API-key requests) — merchant-track
    // callers never supply it in the body, it's forced server-side from the caller's credential,
    // mirroring ShipmentsEndpointsTests.cs / ApiKeyAuthenticationTests.cs's ValidShipmentRequest
    // helpers exactly.
    private static CreateShipmentRequest ValidShipmentRequest(Guid? merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId) => new(
        merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
        "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
        "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 2, 500m) });

    private async Task<Guid> CreateMerchantAsync(HttpClient staffClient)
    {
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "Shipper Pvt. Ltd.", "Shipper", "01-4444444", "shipper@example.com",
            $"shipper-owner-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        var merchant = await response.Content.ReadFromJsonAsync<MerchantResponse>();
        return merchant!.Id;
    }

    // Full setup: a fresh org, a routable branch pair with a rate card, and a merchant owner
    // logged in and ready to POST /api/v1/shipments.
    private async Task<(HttpClient StaffClient, HttpClient OwnerClient, Guid MerchantId, Guid OriginBranchId, Guid DestinationBranchId, Guid ServiceTypeId)> SeedMerchantWithRouteAsync(string label, WebApplicationFactory<Program>? factory = null)
    {
        var host = factory ?? _factory;
        var staffClient = host.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, label);
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var (merchantId, ownerEmail, ownerPassword) = await CreateMerchantAsync(staffClient, label);

        var ownerClient = host.CreateClient();
        var ownerLogin = await ownerClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, ownerPassword));
        ownerLogin.EnsureSuccessStatusCode();
        var ownerAuth = await ownerLogin.Content.ReadFromJsonAsync<MerchantAuthResult>();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAuth!.AccessToken);

        return (staffClient, ownerClient, merchantId, originBranchId, destinationBranchId, serviceTypeId);
    }

    [Fact]
    public async Task MerchantJwt_SameKeySameBody_ReplaysExactResponse_NoSecondShipmentCreated()
    {
        var (_, ownerClient, _, originBranchId, destinationBranchId, serviceTypeId) = await SeedMerchantWithRouteAsync("replay");
        var request = ValidShipmentRequest(Guid.Empty, originBranchId, destinationBranchId, serviceTypeId);

        using var httpRequest1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/shipments") { Content = JsonContent.Create(request) };
        httpRequest1.Headers.Add("Idempotency-Key", "k1");
        var response1 = await ownerClient.SendAsync(httpRequest1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync();
        var shipment1 = await response1.Content.ReadFromJsonAsync<ShipmentResponse>();

        using var httpRequest2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/shipments") { Content = JsonContent.Create(request) };
        httpRequest2.Headers.Add("Idempotency-Key", "k1");
        var response2 = await ownerClient.SendAsync(httpRequest2);
        Assert.Equal(HttpStatusCode.Created, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync();
        var shipment2 = await response2.Content.ReadFromJsonAsync<ShipmentResponse>();

        // Exact same stored response body byte-for-byte (proves replay, not a freshly
        // re-executed handler that happens to produce equal-looking JSON).
        Assert.Equal(body1, body2);
        Assert.Equal(shipment1!.Id, shipment2!.Id);
        Assert.Equal(shipment1.Awb, shipment2.Awb);

        var listResponse = await ownerClient.GetAsync("/api/v1/shipments");
        var shipments = await listResponse.Content.ReadFromJsonAsync<PagedResult<ShipmentResponse>>();
        Assert.Single(shipments!.Items);
    }

    [Fact]
    public async Task MerchantJwt_SameKeyDifferentBody_Returns409()
    {
        var (_, ownerClient, _, originBranchId, destinationBranchId, serviceTypeId) = await SeedMerchantWithRouteAsync("conflict");
        var request1 = ValidShipmentRequest(Guid.Empty, originBranchId, destinationBranchId, serviceTypeId);
        var request2 = request1 with { WeightKg = 0.9m };

        using var httpRequest1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/shipments") { Content = JsonContent.Create(request1) };
        httpRequest1.Headers.Add("Idempotency-Key", "k-conflict");
        var response1 = await ownerClient.SendAsync(httpRequest1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        using var httpRequest2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/shipments") { Content = JsonContent.Create(request2) };
        httpRequest2.Headers.Add("Idempotency-Key", "k-conflict");
        var response2 = await ownerClient.SendAsync(httpRequest2);

        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);
        var payload = await response2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", payload.GetProperty("error").GetProperty("code").GetString());

        // Only the first (accepted) request actually created a shipment.
        var listResponse = await ownerClient.GetAsync("/api/v1/shipments");
        var shipments = await listResponse.Content.ReadFromJsonAsync<PagedResult<ShipmentResponse>>();
        Assert.Single(shipments!.Items);
    }

    [Fact]
    public async Task MerchantJwt_NoIdempotencyKeyHeader_Returns400()
    {
        var (_, ownerClient, _, originBranchId, destinationBranchId, serviceTypeId) = await SeedMerchantWithRouteAsync("missingkey");
        var request = ValidShipmentRequest(Guid.Empty, originBranchId, destinationBranchId, serviceTypeId);

        var response = await ownerClient.PostAsJsonAsync("/api/v1/shipments", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", payload.GetProperty("error").GetProperty("code").GetString());

        // No shipment should have been created.
        var listResponse = await ownerClient.GetAsync("/api/v1/shipments");
        var shipments = await listResponse.Content.ReadFromJsonAsync<PagedResult<ShipmentResponse>>();
        Assert.Empty(shipments!.Items);
    }

    // Regression guard — this is the most important test in this file. If the middleware's
    // GetMerchantId() is not null gate is flipped (or missing), staff-JWT-driven shipment
    // creation everywhere in this codebase (none of which sends an Idempotency-Key header) starts
    // failing with 400 IDEMPOTENCY_KEY_REQUIRED, breaking every existing test in
    // ShipmentsEndpointsTests.cs.
    [Fact]
    public async Task StaffJwt_NoIdempotencyKeyHeader_StillSucceeds()
    {
        var staffClient = _factory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, "staffok");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantId = await CreateMerchantAsync(staffClient);

        var response = await staffClient.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Calling twice with no header, still no enforcement — staff callers are fully exempt,
        // not merely "exempt from the requirement but still deduped."
        var response2 = await staffClient.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        Assert.Equal(HttpStatusCode.Created, response2.StatusCode);

        var listResponse = await staffClient.GetAsync("/api/v1/shipments");
        var shipments = await listResponse.Content.ReadFromJsonAsync<PagedResult<ShipmentResponse>>();
        Assert.Equal(2, shipments!.Items.Count);
    }

    // Proves the middleware's gate is GetMerchantId() is not null — true for both merchant-JWT and
    // API-key callers — not "is this specifically a JWT". Reuses Task 5's API-key fixture pattern.
    [Fact]
    public async Task ApiKeyCaller_SameKeyDifferentBody_Returns409()
    {
        var (_, ownerClient, _, originBranchId, destinationBranchId, serviceTypeId) = await SeedMerchantWithRouteAsync("apikeyidem");

        var createKeyResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys", new CreateApiKeyRequest());
        createKeyResponse.EnsureSuccessStatusCode();
        var apiKey = await createKeyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        var apiKeyClient = _factory.CreateClient();
        apiKeyClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey!.PlaintextKey);

        var request1 = ValidShipmentRequest(null, originBranchId, destinationBranchId, serviceTypeId);
        var request2 = request1 with { WeightKg = 1.1m };

        using var httpRequest1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/shipments") { Content = JsonContent.Create(request1) };
        httpRequest1.Headers.Add("Idempotency-Key", "k-apikey");
        var response1 = await apiKeyClient.SendAsync(httpRequest1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        using var httpRequest2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/shipments") { Content = JsonContent.Create(request2) };
        httpRequest2.Headers.Add("Idempotency-Key", "k-apikey");
        var response2 = await apiKeyClient.SendAsync(httpRequest2);
        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);

        // Missing header via an API-key caller must also 400, same as a merchant JWT caller.
        var noHeaderResponse = await apiKeyClient.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(null, originBranchId, destinationBranchId, serviceTypeId));
        Assert.Equal(HttpStatusCode.BadRequest, noHeaderResponse.StatusCode);

        var listResponse = await ownerClient.GetAsync("/api/v1/shipments");
        var shipments = await listResponse.Content.ReadFromJsonAsync<PagedResult<ShipmentResponse>>();
        Assert.Single(shipments!.Items);
    }

    // Final-review finding (critical): ApiKeyRateLimitFilter is an endpoint filter, so its 429
    // short-circuit happens *inside* the region IdempotencyMiddleware captures and _next returns
    // normally — no exception. Before the fix, the middleware unconditionally cached whatever
    // status came back, so a rate-limited merchant's Idempotency-Key was poisoned with that 429
    // for 24 hours and every subsequent retry replayed the failure (without even a Retry-After
    // header) instead of actually creating the shipment.
    [Fact]
    public async Task ApiKeyCaller_RateLimited429_IsNeverCachedUnderTheIdempotencyKey_AndTheSameKeyStillWorksAfterwards()
    {
        using var scopedFactory = CreateFactoryWithLowApiKeyRateLimit(2);
        var (_, ownerClient, merchantId, originBranchId, destinationBranchId, serviceTypeId) =
            await SeedMerchantWithRouteAsync("ratelimitidem", scopedFactory);

        var firstKeyResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys", new CreateApiKeyRequest());
        firstKeyResponse.EnsureSuccessStatusCode();
        var firstKey = await firstKeyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();
        var firstKeyClient = scopedFactory.CreateClient();
        firstKeyClient.DefaultRequestHeaders.Add("X-Api-Key", firstKey!.PlaintextKey);

        // Burn this key's quota (limit 2) so the shipment POST below is request #3.
        Assert.Equal(HttpStatusCode.OK, (await firstKeyClient.GetAsync("/api/v1/shipments")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await firstKeyClient.GetAsync("/api/v1/shipments")).StatusCode);

        var idempotencyKey = $"rl-{Guid.NewGuid():N}";
        var shipmentRequest = ValidShipmentRequest(null, originBranchId, destinationBranchId, serviceTypeId);

        using var limitedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/shipments") { Content = JsonContent.Create(shipmentRequest) };
        limitedRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        var limitedResponse = await firstKeyClient.SendAsync(limitedRequest);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);

        // The direct negative: nothing at all was written to the idempotency store under this key.
        var idempotencyStore = scopedFactory.Services.GetRequiredService<IIdempotencyStore>();
        Assert.Null(await idempotencyStore.GetAsync($"{merchantId}:{idempotencyKey}"));

        // And the full loop: a second API key for the SAME merchant (a fresh rate-limit partition —
        // RedisRateLimiter partitions per ApiKey.Id, while the idempotency store is keyed per
        // merchant) retrying with the SAME Idempotency-Key actually creates the shipment, proving
        // the key was never poisoned. This avoids waiting out a real 60-second fixed window.
        var secondKeyResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys", new CreateApiKeyRequest());
        secondKeyResponse.EnsureSuccessStatusCode();
        var secondKey = await secondKeyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();
        var secondKeyClient = scopedFactory.CreateClient();
        secondKeyClient.DefaultRequestHeaders.Add("X-Api-Key", secondKey!.PlaintextKey);

        using var retryRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/shipments") { Content = JsonContent.Create(shipmentRequest) };
        retryRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        var retryResponse = await secondKeyClient.SendAsync(retryRequest);
        Assert.Equal(HttpStatusCode.Created, retryResponse.StatusCode);

        // The successful retry IS cached (the fix narrows what gets stored, it doesn't stop
        // storing), so the 2xx replay path still works.
        var storedRecord = await idempotencyStore.GetAsync($"{merchantId}:{idempotencyKey}");
        Assert.NotNull(storedRecord);
        Assert.Equal(StatusCodes.Status201Created, storedRecord!.StatusCode);

        var listAfter = await ownerClient.GetAsync("/api/v1/shipments");
        var shipmentsAfter = await listAfter.Content.ReadFromJsonAsync<PagedResult<ShipmentResponse>>();
        Assert.Single(shipmentsAfter!.Items);
    }
}
