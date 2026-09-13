using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class OrganizationSettingsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OrganizationSettingsEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest($"Settings Org {suffix}", $"settings-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task GetOrganizationSettings_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/settings/organization");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOrganizationSettings_OwnerToken_ReturnsDefaults()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/settings/organization");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OrganizationSettingsDto>();
        Assert.Equal("Asia/Kathmandu", body!.Profile.TimeZoneId);
        Assert.Equal(7, body.Security.RefreshTokenLifetimeDays);
    }

    [Fact]
    public async Task PatchProfile_OwnerToken_UpdatesAndPersists()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var profile = new OrganizationProfileDto("support@example.test", null, null, null, "Asia/Kathmandu", null);

        var patchResponse = await client.PatchAsJsonAsync("/api/v1/settings/organization/profile", profile);
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/v1/settings/organization");
        var body = await getResponse.Content.ReadFromJsonAsync<OrganizationSettingsDto>();
        Assert.Equal("support@example.test", body!.Profile.SupportEmail);
    }
}
