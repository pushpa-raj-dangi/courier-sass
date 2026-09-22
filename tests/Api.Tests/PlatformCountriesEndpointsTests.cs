using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class PlatformCountriesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformCountriesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> SuperAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString()!);
        return client;
    }

    private async Task<HttpClient> TenantOwnerClientAsync()
    {
        var client = _factory.CreateClient();
        var suffix = $"pc-{Guid.NewGuid():N}"[..16];
        var registerRequest = new RegisterOrganizationRequest(
            $"Platform Country Org {suffix}", $"pc-{suffix}", $"owner-{suffix}@example.test", "P@ssw0rd123!", "Owner");
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();
        var registerBody = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registerBody.AccessToken);
        return client;
    }

    [Fact]
    public async Task ListCountries_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/platform/countries");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListCountries_TenantJwtNotSuperAdmin_Returns403()
    {
        var client = await TenantOwnerClientAsync();

        var response = await client.GetAsync("/api/v1/platform/countries");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCountry_TenantJwtNotSuperAdmin_Returns403()
    {
        var client = await TenantOwnerClientAsync();
        var indiaId = GeographyIds.CountryId("IN");

        var response = await client.PatchAsJsonAsync($"/api/v1/platform/countries/{indiaId}", new { isServiced = true, countryZoneId = (Guid?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListCountries_AsSuperAdmin_PaginatesAndSearches()
    {
        var client = await SuperAdminClientAsync();

        var page1 = await client.GetAsync("/api/v1/platform/countries?page=1&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, page1.StatusCode);
        var page1Body = await page1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, page1Body.GetProperty("items").GetArrayLength());

        var search = await client.GetAsync("/api/v1/platform/countries?search=nepal");
        var searchBody = await search.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, searchBody.GetProperty("items").GetArrayLength());
        Assert.Equal("NP", searchBody.GetProperty("items")[0].GetProperty("isoCode").GetString());
    }

    [Fact]
    public async Task UpdateCountry_AsSuperAdmin_TogglesServicedAndAssignsZone()
    {
        var client = await SuperAdminClientAsync();
        var zoneName = $"Zone {Guid.NewGuid():N}"[..12];
        var zoneCreate = await client.PostAsJsonAsync("/api/v1/platform/country-zones", new { name = zoneName, description = (string?)null });
        zoneCreate.EnsureSuccessStatusCode();
        var zone = await zoneCreate.Content.ReadFromJsonAsync<JsonElement>();
        var zoneId = zone.GetProperty("id").GetGuid();
        var qatarId = GeographyIds.CountryId("QA");

        var patch = await client.PatchAsJsonAsync($"/api/v1/platform/countries/{qatarId}", new { isServiced = true, countryZoneId = zoneId });

        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var body = await patch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isServiced").GetBoolean());
        Assert.Equal(zoneName, body.GetProperty("countryZoneName").GetString());
    }

    [Fact]
    public async Task UpdateCountry_UnknownZone_ReturnsBadRequestWithErrorContract()
    {
        var client = await SuperAdminClientAsync();
        var qatarId = GeographyIds.CountryId("QA");

        var response = await client.PatchAsJsonAsync($"/api/v1/platform/countries/{qatarId}", new { isServiced = true, countryZoneId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("COUNTRY_VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateCountry_UnknownId_ReturnsNotFound()
    {
        var client = await SuperAdminClientAsync();

        var response = await client.PatchAsJsonAsync($"/api/v1/platform/countries/{Guid.NewGuid()}", new { isServiced = true, countryZoneId = (Guid?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateCountryZone_DuplicateName_ReturnsBadRequestWithErrorContract()
    {
        var client = await SuperAdminClientAsync();
        var zoneName = $"Zone {Guid.NewGuid():N}"[..12];
        (await client.PostAsJsonAsync("/api/v1/platform/country-zones", new { name = zoneName, description = (string?)null })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/v1/platform/country-zones", new { name = zoneName, description = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("COUNTRY_ZONE_VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateCountryZone_TenantJwtNotSuperAdmin_Returns403()
    {
        var client = await TenantOwnerClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/platform/country-zones", new { name = "X", description = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
