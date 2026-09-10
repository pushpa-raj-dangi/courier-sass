using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Billing.Application;
using Courier.Events.Domain;
using Courier.Identity.Application;
using Courier.Identity.Presentation;
using Courier.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

// Uses the same WebApplicationFactory<Program> fixture every other *EndpointsTests.cs file in
// this project uses (there is no separate "ApiTestFactory" type — see
// OrganizationSuspensionTests.cs), and the real RegisterOrganizationRequest/AuthResult DTOs from
// Courier.Identity.Application (AuthResult: AccessToken/RefreshToken/UserId/OrganizationId,
// camelCase over the wire — no top-level organizationId/accessToken JSON properties as such).
public class BillingEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BillingEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"{label} Org {suffix}", $"{label}-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task GetPlan_NoOrgBillingManagePermission_Returns403()
    {
        // OrganizationOwner holds every permission (per AuthService), so this test proves the
        // ENDPOINT is actually permission-gated by provisioning a lower-privilege CounterStaff
        // user first — following RolesEndpointsTests.cs's DeactivateUser_ThenLoginFails_...
        // pattern exactly: invite a user onto the seeded CounterStaff role (which holds
        // shipment.create/read/update and pricing.read only — see AdditionalRoleSeeds.cs — and
        // notably NOT org.billing.manage), accept the invite via the real outbox-persisted
        // token, log in as that user, and assert the endpoint rejects them.
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "billingperm");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var roles = await (await client.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
        var counterStaffRole = roles!.Single(r => r.Name == "CounterStaff");

        var branchResponse = await client.PostAsJsonAsync("/api/v1/branches", new
        {
            code = $"BR{Guid.NewGuid():N}"[..8],
            name = "Billing Perm Branch",
            type = "Branch",
            provinceId = Courier.Geography.Infrastructure.GeographyIds.ProvinceId("Bagmati"),
            districtId = Courier.Geography.Infrastructure.GeographyIds.DistrictId("Bagmati", "Kathmandu"),
            localLevelId = Courier.Geography.Infrastructure.GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City"),
            wardNumber = 1,
            addressLine = "Somewhere in Kathmandu"
        });
        branchResponse.EnsureSuccessStatusCode();
        var branchId = (await branchResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var staffEmail = $"staff-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/v1/users/invite", new InviteUserRequest(staffEmail, "Staff Person", null, counterStaffRole.Id, branchId, null));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outboxMessage = await dbContext.Set<OutboxMessage>().IgnoreQueryFilters()
            .Where(m => m.EventType == "user.invited")
            .OrderByDescending(m => m.CreatedAt)
            .FirstAsync(m => m.Payload.Contains(staffEmail));
        var payload = JsonDocument.Parse(outboxMessage.Payload).RootElement;
        var inviteUrl = payload.GetProperty("InviteUrl").GetString()!;
        var token = Uri.UnescapeDataString(inviteUrl.Split("token=")[1]);
        await client.PostAsJsonAsync("/api/v1/auth/accept-invite", new AcceptInviteRequest(token, "St@ffPassword1"));

        var staffClient = _factory.CreateClient();
        var loginResponse = await staffClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(staffEmail, "St@ffPassword1"));
        var staffAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffAuth!.AccessToken);

        var response = await staffClient.GetAsync("/api/v1/billing/plan");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPlan_AsOwnerOnDefaultPlan_ReturnsPlanSummary()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "billingplan");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        using var scope = _factory.Services.CreateScope();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<SubscriptionService>();
        var planService = scope.ServiceProvider.GetRequiredService<PlanService>();
        var plan = await planService.CreatePlanAsync(new CreatePlanRequest("Free", $"free-{Guid.NewGuid():N}", 0m, null, null, null, true), Guid.NewGuid());
        await subscriptionService.AssignPlanAsync(org.OrganizationId, plan.Id);

        var response = await client.GetAsync("/api/v1/billing/plan");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Free", body.GetProperty("planName").GetString());
    }

}
