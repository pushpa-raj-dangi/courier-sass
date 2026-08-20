using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class BranchesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    private static readonly List<Guid> KathmanduWard1Areas = new()
    {
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area A"),
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area B"),
    };

    private static CreateBranchRequest ValidBranchRequest(string code) => new(
        code, $"Branch {code}", BranchType.Hub,
        KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
        "New Road, Kathmandu", null, null, null, null, null);

    public BranchesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Branches Org {suffix}", $"branches-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task PostBranches_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "KTM-HUB", "Kathmandu Hub", BranchType.Hub,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "New Road, Kathmandu", null, null, null, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateThenGet_OwnerWithAllBranchesScope_Succeeds()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "KTM-HUB", "Kathmandu Hub", BranchType.Hub,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "New Road, Kathmandu", "01-4444444", "09:00-18:00", 27.7m, 85.3m, null));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<BranchResponse>();
        Assert.NotNull(created);

        var getResponse = await client.GetAsync($"/api/v1/branches/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<BranchResponse>();
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal("KTM-HUB", fetched.Code);
    }

    [Fact]
    public async Task CreateBranch_DuplicateCode_Returns400()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var request = new CreateBranchRequest(
            "DUP-01", "First", BranchType.Branch,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Somewhere", null, null, null, null, null);
        await client.PostAsJsonAsync("/api/v1/branches", request);

        var response = await client.PostAsJsonAsync("/api/v1/branches", request with { Name = "Second" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateBranch_MismatchedGeographyChain_Returns400()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        // Kathmandu's province/district paired with Pokhara's local level — mismatched chain.
        var pokharaLocalLevelId = GeographyIds.LocalLevelId("Gandaki", "Kaski", "Pokhara Metropolitan City");
        var response = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "BAD-GEO-01", "Bad Geo Branch", BranchType.Branch,
            KathmanduProvinceId, KathmanduDistrictId, pokharaLocalLevelId, KathmanduWardNumber,
            "Somewhere", null, null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("valid geography chain", body);
    }

    [Fact]
    public async Task GetBranch_NonExistent_Returns404()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync($"/api/v1/branches/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListBranches_ReturnsCreatedBranch()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "LST-01", "List Test Branch", BranchType.Branch,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Somewhere", null, null, null, null, null));

        var response = await client.GetAsync("/api/v1/branches");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var branches = await response.Content.ReadFromJsonAsync<List<BranchResponse>>();
        Assert.Contains(branches!, b => b.Code == "LST-01");
    }

    [Fact]
    public async Task AssignDeliveryZone_UnknownZoneId_Returns400()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest("KTM-ZONE-BAD"));
        var branch = await createResponse.Content.ReadFromJsonAsync<BranchResponse>();

        var response = await client.PutAsJsonAsync($"/api/v1/branches/{branch!.Id}/delivery-zone", new AssignDeliveryZoneRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AssignDeliveryZone_ValidZone_UpdatesBranch()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest("KTM-ZONE-OK"));
        var branch = await createResponse.Content.ReadFromJsonAsync<BranchResponse>();

        var zoneResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest("Zone A", KathmanduWard1Areas));
        var zone = await zoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();

        var response = await client.PutAsJsonAsync($"/api/v1/branches/{branch!.Id}/delivery-zone", new AssignDeliveryZoneRequest(zone!.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<BranchResponse>();
        Assert.Equal(zone.Id, updated!.DeliveryZoneId);
    }
}
