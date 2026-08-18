using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Application;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class GeographyEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

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
}
