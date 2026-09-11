using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Billing.Application;
using Courier.Identity.Application;
using Courier.Platform.Presentation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task AssignPlan_AsSuperAdmin_UpdatesOrganizationSubscription()
    {
        var client = _factory.CreateClient();
        var suffix = $"platform-assign-{Guid.NewGuid():N}"[..24];
        var registerRequest = new RegisterOrganizationRequest(
            $"Platform Assign Org {suffix}", $"pa-{suffix}", $"owner-{suffix}@example.test", "P@ssw0rd123!", "Owner");
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();
        var org = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;

        Guid planId;
        using (var scope = _factory.Services.CreateScope())
        {
            var planService = scope.ServiceProvider.GetRequiredService<PlanService>();
            var plan = await planService.CreatePlanAsync(new CreatePlanRequest("PlatformAssigned", $"platform-assigned-{Guid.NewGuid():N}", 750m, null, null, null, false), Guid.NewGuid());
            planId = plan.Id;
        }

        var superAdminToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var response = await client.PostAsJsonAsync($"/api/v1/platform/organizations/{org.OrganizationId}/plan", new AssignOrganizationPlanRequest(planId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task List_AsSuperAdmin_SearchFilterAndPageReachTheService()
    {
        var client = _factory.CreateClient();
        var prefix = $"ListQuery-{Guid.NewGuid():N}";
        var suffixA = $"{prefix}-a";
        var suffixB = $"{prefix}-b";
        var registerA = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterOrganizationRequest(
            $"{prefix} Org A", $"lq-a-{Guid.NewGuid():N}", $"owner-{suffixA}@example.test", "P@ssw0rd123!", "Owner"));
        registerA.EnsureSuccessStatusCode();
        var registerB = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterOrganizationRequest(
            $"{prefix} Org B", $"lq-b-{Guid.NewGuid():N}", $"owner-{suffixB}@example.test", "P@ssw0rd123!", "Owner"));
        registerB.EnsureSuccessStatusCode();

        var superAdminToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        // search narrows to just these two newly-registered orgs, both freshly Active (never
        // suspended/deleted), regardless of how many other organizations exist in the shared DB.
        var searchResponse = await client.GetAsync($"/api/v1/platform/organizations?search={Uri.EscapeDataString(prefix)}");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var searchBody = await searchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, searchBody.GetProperty("totalCount").GetInt32());

        // page/pageSize narrows the returned page to 1 row while totalCount still reflects both.
        var pagedResponse = await client.GetAsync($"/api/v1/platform/organizations?search={Uri.EscapeDataString(prefix)}&page=1&pageSize=1");
        var pagedBody = await pagedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, pagedBody.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, pagedBody.GetProperty("items").GetArrayLength());
        Assert.Equal(1, pagedBody.GetProperty("page").GetInt32());
        Assert.Equal(1, pagedBody.GetProperty("pageSize").GetInt32());

        // isActive:false excludes both (they're freshly registered and Active).
        var inactiveOnlyResponse = await client.GetAsync($"/api/v1/platform/organizations?search={Uri.EscapeDataString(prefix)}&isActive=false");
        var inactiveOnlyBody = await inactiveOnlyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, inactiveOnlyBody.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task List_AsRegularStaff_Returns403()
    {
        var client = _factory.CreateClient();
        var suffix = $"list-forbidden-{Guid.NewGuid():N}"[..30];
        var registerRequest = new RegisterOrganizationRequest(
            $"List Forbidden Org {suffix}", $"lf-{suffix}", $"owner-{suffix}@example.test", "P@ssw0rd123!", "Owner");
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();
        var org = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync("/api/v1/platform/organizations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AssignPlan_AsRegularStaff_Returns403()
    {
        var client = _factory.CreateClient();
        // The literal prefix below is 26 chars, longer than the [..24] slice other tests in this
        // file use for their suffix — that slice would drop the GUID entirely and leave a fixed,
        // non-unique string, causing a duplicate-slug 400 on register after the first run against
        // the shared Postgres. Widen the slice enough to retain real uniqueness from the GUID.
        var suffix = $"platform-assign-forbidden-{Guid.NewGuid():N}"[..34];
        var registerRequest = new RegisterOrganizationRequest(
            $"Platform Assign Forbidden Org {suffix}", $"paf-{suffix}", $"owner-{suffix}@example.test", "P@ssw0rd123!", "Owner");
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();
        var org = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.PostAsJsonAsync($"/api/v1/platform/organizations/{org.OrganizationId}/plan", new AssignOrganizationPlanRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // The org detail Sheet's three nested sections (users/branches/invoices) — each its own
    // independently-paginated sub-resource route, see PlatformDashboardEndpoints.cs.

    [Fact]
    public async Task ListUsers_AsSuperAdmin_ReturnsPagedRegisteredOwner()
    {
        var client = _factory.CreateClient();
        var suffix = $"nested-users-{Guid.NewGuid():N}"[..24];
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterOrganizationRequest(
            $"Nested Users Org {suffix}", $"nu-{suffix}", $"owner-{suffix}@example.test", "P@ssw0rd123!", "Owner"));
        registerResponse.EnsureSuccessStatusCode();
        var org = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;

        var superAdminToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var response = await client.GetAsync($"/api/v1/platform/organizations/{org.OrganizationId}/users?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal($"owner-{suffix}@example.test", item.GetProperty("email").GetString());
    }

    [Fact]
    public async Task ListBranches_AsSuperAdmin_ReturnsEmptyPagedResultForFreshOrganization()
    {
        var client = _factory.CreateClient();
        var suffix = $"nested-branches-{Guid.NewGuid():N}"[..24];
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterOrganizationRequest(
            $"Nested Branches Org {suffix}", $"nb-{suffix}", $"owner-{suffix}@example.test", "P@ssw0rd123!", "Owner"));
        registerResponse.EnsureSuccessStatusCode();
        var org = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;

        var superAdminToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var response = await client.GetAsync($"/api/v1/platform/organizations/{org.OrganizationId}/branches?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("totalCount").GetInt32());
        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task ListInvoices_AsSuperAdmin_ReturnsEmptyPagedResultForFreshOrganization()
    {
        var client = _factory.CreateClient();
        var suffix = $"nested-invoices-{Guid.NewGuid():N}"[..24];
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterOrganizationRequest(
            $"Nested Invoices Org {suffix}", $"ni-{suffix}", $"owner-{suffix}@example.test", "P@ssw0rd123!", "Owner"));
        registerResponse.EnsureSuccessStatusCode();
        var org = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;

        var superAdminToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var response = await client.GetAsync($"/api/v1/platform/organizations/{org.OrganizationId}/invoices?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("totalCount").GetInt32());
        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task ListUsers_AsRegularStaff_Returns403()
    {
        var client = _factory.CreateClient();
        var suffix = $"nested-forbidden-{Guid.NewGuid():N}"[..24];
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterOrganizationRequest(
            $"Nested Forbidden Org {suffix}", $"nf-{suffix}", $"owner-{suffix}@example.test", "P@ssw0rd123!", "Owner"));
        registerResponse.EnsureSuccessStatusCode();
        var org = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync($"/api/v1/platform/organizations/{org.OrganizationId}/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
