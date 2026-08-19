using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class MerchantsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public MerchantsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}"[..12];
        var request = new RegisterOrganizationRequest(
            $"Merchant Org {suffix}", $"merchant-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private static CreateMerchantRequest ValidMerchantRequest(string ownerEmail) => new(
        "Acme Traders Pvt. Ltd.", "Acme Traders", "01-4444444", "contact@acme.example",
        ownerEmail, "P@ssw0rd123!", "Merchant Owner",
        KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu");

    [Fact]
    public async Task PostMerchants_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/merchants", ValidMerchantRequest("noauth@example.com"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateThenGet_StaffWithOwnerRole_Succeeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "a");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/merchants", ValidMerchantRequest("merchant-owner-a@example.com"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<MerchantResponse>();
        Assert.NotNull(created);
        Assert.Equal("Acme Traders", created!.DisplayName);

        var getResponse = await client.GetAsync($"/api/v1/merchants/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task CreateMerchant_MismatchedGeographyChain_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "b");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var pokharaLocalLevelId = GeographyIds.LocalLevelId("Gandaki", "Kaski", "Pokhara Metropolitan City");
        var request = ValidMerchantRequest("bad-geo@example.com") with { LocalLevelId = pokharaLocalLevelId };

        var response = await client.PostAsJsonAsync("/api/v1/merchants", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("valid geography chain", body);
    }

    [Fact]
    public async Task ListMerchants_OrgBCannotSeeOrgAsMerchant()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "c");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var createResponse = await clientA.PostAsJsonAsync("/api/v1/merchants", ValidMerchantRequest("merchant-owner-c@example.com"));
        var created = await createResponse.Content.ReadFromJsonAsync<MerchantResponse>();

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "d");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var getResponse = await clientB.GetAsync($"/api/v1/merchants/{created!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var listResponse = await clientB.GetAsync("/api/v1/merchants");
        var orgBMerchants = await listResponse.Content.ReadFromJsonAsync<List<MerchantResponse>>();
        Assert.DoesNotContain(orgBMerchants!, m => m.Id == created.Id);
    }
}
