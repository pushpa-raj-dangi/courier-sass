using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class CountriesGeographyEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public CountriesGeographyEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Country Org {suffix}", $"country-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private async Task<(string OwnerEmail, string Password)> CreateMerchantAsync(HttpClient staffClient)
    {
        var ownerEmail = $"country-owner-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            "Country Traders Pvt. Ltd.", "Country Traders", "01-4444444", "contact@country.example",
            ownerEmail, "P@ssw0rd123!", "Merchant Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu");
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", request);
        response.EnsureSuccessStatusCode();
        return (ownerEmail, "P@ssw0rd123!");
    }

    private async Task<HttpClient> SuperAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString()!);
        return client;
    }

    [Fact]
    public async Task GetCountries_Unauthenticated_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/geography/countries");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Country rows are platform-level singletons shared by the whole test suite and are never
    // reset between runs against the persistent courier-postgres-1 container (unlike an
    // Organization, which each test creates fresh) — so this must NOT assert anything about a
    // country's state *before* this test's own PATCH (a prior run may have already toggled it).
    // Only idempotent, order-independent assertions belong here.
    [Fact]
    public async Task GetCountries_StaffToken_ReturnsOnlyServicedCountries()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var superAdmin = await SuperAdminClientAsync();
        var maldivesId = GeographyIds.CountryId("MV");
        var patch = await superAdmin.PatchAsJsonAsync($"/api/v1/platform/countries/{maldivesId}", new { isServiced = true, countryZoneId = (Guid?)null });
        patch.EnsureSuccessStatusCode();

        var countries = await (await client.GetAsync("/api/v1/geography/countries")).Content.ReadFromJsonAsync<List<CountryDto>>();
        Assert.Contains(countries!, c => c.IsoCode == "MV");
        Assert.All(countries!, c => Assert.True(c.IsServiced));
    }

    [Fact]
    public async Task GetCountries_MerchantOwnerToken_Succeeds()
    {
        var staffClient = _factory.CreateClient();
        var staffAuth = await RegisterOrgAsync(staffClient);
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffAuth.AccessToken);
        var (ownerEmail, password) = await CreateMerchantAsync(staffClient);

        var merchantClient = _factory.CreateClient();
        var loginResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, password));
        var merchantAuth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", merchantAuth!.AccessToken);

        var response = await merchantClient.GetAsync("/api/v1/geography/countries");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
