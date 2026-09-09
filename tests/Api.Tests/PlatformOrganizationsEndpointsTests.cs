using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

// Uses the same WebApplicationFactory<Program> fixture every other endpoint test in this project
// uses (there is no separate "ApiTestFactory" type here — see OrganizationSuspensionTests.cs/
// PlatformAuthEndpointsTests.cs). The register endpoint's response is the real AuthResult DTO
// (AccessToken/RefreshToken/UserId/OrganizationId, Courier.Identity.Application) — it has no
// `ownerEmail` JSON property, so the owner's email is taken from the request this test itself
// sends, matching OrganizationSuspensionTests.cs's established pattern.
public class PlatformOrganizationsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformOrganizationsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> LoginAsSuperAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task Suspend_TenantJwtNotSuperAdmin_Returns403()
    {
        var client = _factory.CreateClient();
        var suffix = $"nsa-{Guid.NewGuid():N}"[..16];
        var registerRequest = new RegisterOrganizationRequest(
            $"Not Super Admin Org {suffix}", $"nsa-{suffix}", $"owner-{suffix}@example.test", "P@ssw0rd123!", "Owner");

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();
        var registerBody = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", registerBody.AccessToken);
        var response = await client.PostAsync($"/api/v1/platform/organizations/{registerBody.OrganizationId}/suspend", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuspendThenLogin_TenantOwnerLoginRejectedWithOrgSuspended()
    {
        var client = _factory.CreateClient();
        var suffix = $"suspend-flow-{Guid.NewGuid():N}"[..24];
        var ownerEmail = $"owner-{suffix}@example.test";
        var registerRequest = new RegisterOrganizationRequest(
            $"Suspend Flow Org {suffix}", $"suspend-flow-{suffix}", ownerEmail, "P@ssw0rd123!", "Owner");

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();
        var registerBody = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;
        var organizationId = registerBody.OrganizationId;

        var superAdminToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", superAdminToken);
        var suspendResponse = await client.PostAsync($"/api/v1/platform/organizations/{organizationId}/suspend", null);
        Assert.Equal(HttpStatusCode.NoContent, suspendResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(ownerEmail, "P@ssw0rd123!"));

        Assert.Equal(HttpStatusCode.Forbidden, loginResponse.StatusCode);
    }
}
