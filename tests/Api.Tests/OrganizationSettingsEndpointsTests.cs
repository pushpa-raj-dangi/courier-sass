using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

    [Fact]
    public async Task PatchSecurity_InvalidRefreshDays_Returns400()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.PatchAsJsonAsync("/api/v1/settings/organization/security", new SecurityPolicyDto(0, 8));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ORGANIZATION_SETTINGS_VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task PatchSecurity_ValidValues_UpdatesSettings()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.PatchAsJsonAsync("/api/v1/settings/organization/security", new SecurityPolicyDto(14, 10));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var getResponse = await client.GetAsync("/api/v1/settings/organization");
        var body = await getResponse.Content.ReadFromJsonAsync<OrganizationSettingsDto>();
        Assert.Equal(14, body!.Security.RefreshTokenLifetimeDays);
    }

    [Fact]
    public async Task RevokeSessions_ExistingRefreshToken_IsRevoked()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var revokeResponse = await client.PostAsync("/api/v1/settings/organization/revoke-sessions", null);
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        // The owner's own already-issued refresh token from registration must now be revoked —
        // exercised end-to-end via the public /auth/refresh endpoint rather than reaching into the
        // database, matching how this test suite already treats RefreshToken as an implementation
        // detail elsewhere.
        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }
}
