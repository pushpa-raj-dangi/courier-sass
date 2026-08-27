using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Courier.Api.Tests;

// Covers Task 6: ApiKeyRateLimitFilter (self-selecting on the api_key_scopes claim, Task 5) and
// its wiring onto the Shipments group. Uses a scoped WebApplicationFactory with
// RateLimiting:ApiKeyRequestsPerMinute overridden to a small number so the test doesn't need 60
// real requests to trip the limit. Mirrors ApiKeyAuthenticationTests.cs's helper conventions.
public class RateLimitingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public RateLimitingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // Scoped host with a small ApiKeyRequestsPerMinute limit so the test can trip the limiter in
    // three requests instead of the real default of 60.
    private WebApplicationFactory<Program> CreateFactoryWithLowLimit(int limit) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:ApiKeyRequestsPerMinute"] = limit.ToString()
            })));

    private async Task<string> RegisterOrgAndGetStaffTokenAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid().ToString("N")[..12]}";
        var request = new RegisterOrganizationRequest(
            $"RateLimit Org {suffix}", $"ratelimit-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        return auth.AccessToken;
    }

    private async Task<(string OwnerEmail, string Password)> CreateMerchantAsync(HttpClient staffClient, string label)
    {
        var ownerEmail = $"owner-{label}-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            "RateLimit Traders Pvt. Ltd.", "RateLimit Traders", "01-4444444", "contact@ratelimit.example",
            ownerEmail, "P@ssw0rd123!", "Merchant Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu");
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", request);
        response.EnsureSuccessStatusCode();
        return (ownerEmail, "P@ssw0rd123!");
    }

    // Sets up a fresh org (staff) and a merchant owned by it, using the given host, and hands
    // back an X-Api-Key-authenticated HttpClient for the merchant's default (all-scoped) API key.
    private async Task<HttpClient> CreateApiKeyClientAsync(WebApplicationFactory<Program> factory, string label)
    {
        var staffClient = factory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, label);
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (ownerEmail, ownerPassword) = await CreateMerchantAsync(staffClient, label);

        var ownerClient = factory.CreateClient();
        var ownerLogin = await ownerClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, ownerPassword));
        ownerLogin.EnsureSuccessStatusCode();
        var ownerAuth = await ownerLogin.Content.ReadFromJsonAsync<MerchantAuthResult>();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAuth!.AccessToken);

        var createKeyResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/api-keys", new CreateApiKeyRequest());
        createKeyResponse.EnsureSuccessStatusCode();
        var apiKey = await createKeyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        var apiKeyClient = factory.CreateClient();
        apiKeyClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey!.PlaintextKey);
        return apiKeyClient;
    }

    [Fact]
    public async Task ApiKeyCaller_ExceedsConfiguredLimit_ThirdRequestReturns429WithRetryAfterAndErrorCode()
    {
        using var scopedFactory = CreateFactoryWithLowLimit(2);
        var apiKeyClient = await CreateApiKeyClientAsync(scopedFactory, "apikeylimit");

        var first = await apiKeyClient.GetAsync("/api/v1/shipments");
        var second = await apiKeyClient.GetAsync("/api/v1/shipments");
        var third = await apiKeyClient.GetAsync("/api/v1/shipments");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);

        Assert.True(third.Headers.TryGetValues("Retry-After", out var retryAfterValues), "Expected a Retry-After header on the 429 response.");
        var retryAfter = Assert.Single(retryAfterValues!);
        Assert.True(int.TryParse(retryAfter, out var retryAfterSeconds), $"Retry-After value '{retryAfter}' was not numeric.");
        Assert.True(retryAfterSeconds > 0);

        var body = await third.Content.ReadAsStringAsync();
        Assert.Contains("RATE_LIMIT_EXCEEDED", body);
    }

    [Fact]
    public async Task StaffJwtCaller_OnSameOverriddenConfigFactory_IsNeverRateLimited()
    {
        // Same low-limit override as the ApiKey test above, but the caller here authenticates
        // with a staff JWT (no api_key_scopes claim) — proves ApiKeyRateLimitFilter's
        // self-selecting check skips JWT callers entirely rather than merely defaulting to a
        // higher limit that this test would fail to distinguish from "not rate limited".
        using var scopedFactory = CreateFactoryWithLowLimit(2);
        var staffClient = scopedFactory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, "staffnolimit");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        for (var i = 0; i < 5; i++)
        {
            var response = await staffClient.GetAsync("/api/v1/shipments");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
