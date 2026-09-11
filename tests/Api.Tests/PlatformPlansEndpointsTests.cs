using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

// Uses the same WebApplicationFactory<Program> fixture every other *EndpointsTests.cs file in
// this project uses (there is no separate "ApiTestFactory" type — see
// OrganizationSuspensionTests.cs/BillingEndpointsTests.cs), and the real RegisterOrganizationRequest/
// AuthResult DTOs from Courier.Identity.Application.
public class PlatformPlansEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformPlansEndpointsTests(WebApplicationFactory<Program> factory)
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
    public async Task CreatePlan_AsSuperAdmin_Succeeds()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/platform/plans", new { name = "Test Plan", slug = $"test-{Guid.NewGuid():N}", monthlyPriceNpr = 2999m, maxBranches = 5, maxStaffUsers = (int?)null, maxMonthlyShipments = (int?)null, isDefault = false });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ListPlans_AsSuperAdmin_ReturnsCreatedPlan()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var slug = $"list-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync("/api/v1/platform/plans", new { name = "List Test Plan", slug, monthlyPriceNpr = 1500m, maxBranches = (int?)null, maxStaffUsers = (int?)null, maxMonthlyShipments = (int?)null, isDefault = false });
        createResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/v1/platform/plans?search={slug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(body.GetProperty("items").EnumerateArray(), p => p.GetProperty("slug").GetString() == slug);
    }

    [Fact]
    public async Task ListPlans_SearchAndPagination_NarrowsAndPagesResults()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var marker = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 3; i++)
        {
            var createResponse = await client.PostAsJsonAsync("/api/v1/platform/plans", new { name = $"Search Test {marker} {i}", slug = $"search-{marker}-{i}", monthlyPriceNpr = 100m * i, maxBranches = (int?)null, maxStaffUsers = (int?)null, maxMonthlyShipments = (int?)null, isDefault = false });
            createResponse.EnsureSuccessStatusCode();
        }

        var response = await client.GetAsync($"/api/v1/platform/plans?search={marker}&page=1&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
        Assert.Equal(3, body.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task RetirePlan_NonDefaultActivePlan_Succeeds()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResponse = await client.PostAsJsonAsync("/api/v1/platform/plans", new { name = "Retire Test Plan", slug = $"retire-{Guid.NewGuid():N}", monthlyPriceNpr = 999m, maxBranches = (int?)null, maxStaffUsers = (int?)null, maxMonthlyShipments = (int?)null, isDefault = false });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var planId = created.GetProperty("id").GetGuid();

        var response = await client.PostAsync($"/api/v1/platform/plans/{planId}/retire", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task CreatePlan_TenantJwtNotSuperAdmin_Returns403()
    {
        var client = _factory.CreateClient();
        var suffix = $"nsa-{Guid.NewGuid():N}"[..16];
        var registerRequest = new RegisterOrganizationRequest(
            $"Not Super Admin Org {suffix}", $"nsa-{suffix}", $"owner-{suffix}@example.test", "P@ssw0rd123!", "Owner");
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();
        var registerBody = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registerBody.AccessToken);
        var response = await client.PostAsJsonAsync("/api/v1/platform/plans", new { name = "Test Plan", slug = $"test-{Guid.NewGuid():N}", monthlyPriceNpr = 2999m, maxBranches = (int?)null, maxStaffUsers = (int?)null, maxMonthlyShipments = (int?)null, isDefault = false });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListPlans_TenantJwtNotSuperAdmin_Returns403()
    {
        var client = _factory.CreateClient();
        var suffix = $"nsa-list-{Guid.NewGuid():N}"[..16];
        var registerRequest = new RegisterOrganizationRequest(
            $"Not Super Admin List Org {suffix}", $"nsa-list-{suffix}", $"owner-{suffix}@example.test", "P@ssw0rd123!", "Owner");
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();
        var registerBody = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registerBody.AccessToken);
        var response = await client.GetAsync("/api/v1/platform/plans");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
