using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Application;
using Courier.Identity.Application;
using Courier.Geography.Infrastructure;
using Courier.Merchants.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class GeographyEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public GeographyEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Geo Org {suffix}", $"geo-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    // Mirrors MerchantAuthAndStaffEndpointsTests.cs's CreateMerchantAsync — hands back an Owner
    // login so a test can drive the merchant JWT track independently of staffClient.
    private async Task<(string OwnerEmail, string Password)> CreateMerchantAsync(HttpClient staffClient)
    {
        var ownerEmail = $"geo-owner-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            "Geo Traders Pvt. Ltd.", "Geo Traders", "01-4444444", "contact@geo.example",
            ownerEmail, "P@ssw0rd123!", "Merchant Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu");
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", request);
        response.EnsureSuccessStatusCode();
        return (ownerEmail, "P@ssw0rd123!");
    }

    [Fact]
    public async Task GetProvinces_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/geography/provinces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProvinces_Authenticated_ReturnsAllSeven()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/geography/provinces");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var provinces = await response.Content.ReadFromJsonAsync<List<ProvinceResponse>>();
        Assert.Equal(7, provinces!.Count);
    }

    [Fact]
    public async Task GetDistricts_FiltersByProvince()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var provinces = await (await client.GetAsync("/api/v1/geography/provinces")).Content.ReadFromJsonAsync<List<ProvinceResponse>>();
        var bagmati = provinces!.Single(p => p.Name == "Bagmati");

        var response = await client.GetAsync($"/api/v1/geography/districts?provinceId={bagmati.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var districts = await response.Content.ReadFromJsonAsync<List<DistrictResponse>>();
        Assert.Equal(13, districts!.Count);
        Assert.Contains(districts, d => d.Name == "Kathmandu");
    }

    [Fact]
    public async Task GetLocalLevels_ThenWards_ThenAreas_WalksFullChain()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var provinces = await (await client.GetAsync("/api/v1/geography/provinces")).Content.ReadFromJsonAsync<List<ProvinceResponse>>();
        var bagmati = provinces!.Single(p => p.Name == "Bagmati");
        var districts = await (await client.GetAsync($"/api/v1/geography/districts?provinceId={bagmati.Id}")).Content.ReadFromJsonAsync<List<DistrictResponse>>();
        var kathmandu = districts!.Single(d => d.Name == "Kathmandu");

        var localLevelsResponse = await client.GetAsync($"/api/v1/geography/local-levels?districtId={kathmandu.Id}");
        Assert.Equal(HttpStatusCode.OK, localLevelsResponse.StatusCode);
        // Task 16 E2E-discovered bug: LocalLevelType had no JsonStringEnumConverter, so it
        // serialized as a raw integer (0) — the frontend's LOCAL_LEVEL_TYPE_LABEL[l.type] lookup
        // (keyed by the string union names) silently missed, rendering "Kathmandu Metropolitan
        // City ()" with an empty type suffix in both the staff and merchant GeographyPicker.
        // Asserting on the raw JSON text (not the round-tripped LocalLevelResponse.Type, which
        // would deserialize correctly either way under the test client's own default options)
        // is what actually proves the wire format changed.
        var rawJson = await localLevelsResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"Metropolitan\"", rawJson);
        var localLevels = await localLevelsResponse.Content.ReadFromJsonAsync<List<LocalLevelResponse>>();
        var kathmanduMetro = Assert.Single(localLevels!);
        Assert.Equal("Kathmandu Metropolitan City", kathmanduMetro.Name);

        var wardsResponse = await client.GetAsync($"/api/v1/geography/wards?localLevelId={kathmanduMetro.Id}");
        Assert.Equal(HttpStatusCode.OK, wardsResponse.StatusCode);
        var wards = await wardsResponse.Content.ReadFromJsonAsync<List<WardResponse>>();
        Assert.Equal(3, wards!.Count);

        var areasResponse = await client.GetAsync($"/api/v1/geography/areas?wardId={wards[0].Id}");
        Assert.Equal(HttpStatusCode.OK, areasResponse.StatusCode);
        var areas = await areasResponse.Content.ReadFromJsonAsync<List<AreaResponse>>();
        Assert.Equal(2, areas!.Count);
    }

    // Task 16 E2E-discovered bug: the merchant booking form's Recipient/Sender GeographyPicker
    // calls these same routes, but they were staff-only (RequirePermission("geography.read")
    // resolves a merchant user id against staff RolePermission tables, where no row exists) —
    // a merchant Owner/Staff JWT got 403 for every province/district/local-level lookup, so the
    // Recipient/Sender address section of the booking Sheet could never be filled in at all.
    [Fact]
    public async Task GetProvinces_MerchantOwnerToken_Succeeds()
    {
        var staffClient = _factory.CreateClient();
        var staffAuth = await RegisterOrgAsync(staffClient);
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffAuth.AccessToken);
        var (ownerEmail, password) = await CreateMerchantAsync(staffClient);

        var merchantClient = _factory.CreateClient();
        var loginResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, password));
        var merchantAuth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", merchantAuth!.AccessToken);

        var response = await merchantClient.GetAsync("/api/v1/geography/provinces");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var provinces = await response.Content.ReadFromJsonAsync<List<ProvinceResponse>>();
        Assert.Equal(7, provinces!.Count);
    }

    [Fact]
    public async Task GetDistricts_MerchantOwnerToken_FiltersByProvince()
    {
        var staffClient = _factory.CreateClient();
        var staffAuth = await RegisterOrgAsync(staffClient);
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffAuth.AccessToken);
        var (ownerEmail, password) = await CreateMerchantAsync(staffClient);

        var merchantClient = _factory.CreateClient();
        var loginResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, password));
        var merchantAuth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", merchantAuth!.AccessToken);

        var response = await merchantClient.GetAsync($"/api/v1/geography/districts?provinceId={KathmanduProvinceId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var districts = await response.Content.ReadFromJsonAsync<List<DistrictResponse>>();
        Assert.Contains(districts!, d => d.Name == "Kathmandu");
    }
}
