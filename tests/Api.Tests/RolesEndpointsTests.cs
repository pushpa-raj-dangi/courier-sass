using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Events.Domain;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Identity.Presentation;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

public class RolesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RolesEndpointsTests(WebApplicationFactory<Program> factory)
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
    public async Task GetPermissions_ReturnsGroupedCatalogIncludingShipmentGroup()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "perm");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync("/api/v1/permissions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var groups = await response.Content.ReadFromJsonAsync<List<PermissionGroupResponse>>();
        var shipmentGroup = groups!.Single(g => g.Group == "shipment");
        Assert.Contains(shipmentGroup.Permissions, p => p.Key == "shipment.create");
    }

    [Fact]
    public async Task CreateRole_ThenList_ThenUpdate_ThenDelete_FullLifecycle()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "rolelifecycle");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var created = await (await client.PostAsJsonAsync("/api/v1/roles",
            new CreateRoleRequest("CustomSupport", new[] { "shipment.read", "ndr.read" }))).Content.ReadFromJsonAsync<RoleResponse>();
        Assert.False(created!.IsSystemDefault);

        var listResponse = await client.GetAsync("/api/v1/roles");
        var roles = await listResponse.Content.ReadFromJsonAsync<List<RoleResponse>>();
        Assert.Contains(roles!, r => r.Id == created.Id);
        Assert.True(roles!.Count >= 15); // 14 seeded + this one

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/roles/{created.Id}", new UpdateRoleRequest(new[] { "cod.read" }));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<RoleResponse>();
        Assert.Equal(new[] { "cod.read" }, updated!.PermissionKeys);

        var deleteResponse = await client.DeleteAsync($"/api/v1/roles/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        var afterDelete = await (await client.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
        Assert.DoesNotContain(afterDelete!, r => r.Id == created.Id);
    }

    [Fact]
    public async Task DeleteRole_SystemDefault_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "roledeletedefault");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var roles = await (await client.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
        var ownerRole = roles!.Single(r => r.Name == "OrganizationOwner");

        var response = await client.DeleteAsync($"/api/v1/roles/{ownerRole.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListRoles_DoesNotLeakAnotherOrganizationsCustomRole()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "roletenantA");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var createdInA = await (await clientA.PostAsJsonAsync("/api/v1/roles",
            new CreateRoleRequest("OnlyInOrgA", new[] { "shipment.read" }))).Content.ReadFromJsonAsync<RoleResponse>();

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "roletenantB");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var rolesInB = await (await clientB.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
        Assert.DoesNotContain(rolesInB!, r => r.Id == createdInA!.Id);
    }

    [Fact]
    public async Task GrantBranch_ThenList_ThenRevoke_FullLifecycle()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "grantlifecycle");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var branchId = await CreateBranchAsync(client);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);

        var granted = await (await client.PostAsJsonAsync($"/api/v1/users/{org.UserId}/branches",
            new GrantBranchRequest(branchId, expiresAt))).Content.ReadFromJsonAsync<UserBranchGrantResponse>();
        Assert.Equal(branchId, granted!.BranchId);

        var grants = await (await client.GetAsync($"/api/v1/users/{org.UserId}/branches")).Content.ReadFromJsonAsync<List<UserBranchGrantResponse>>();
        Assert.Contains(grants!, g => g.BranchId == branchId);

        var revokeResponse = await client.DeleteAsync($"/api/v1/users/{org.UserId}/branches/{branchId}");
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);
        var afterRevoke = await (await client.GetAsync($"/api/v1/users/{org.UserId}/branches")).Content.ReadFromJsonAsync<List<UserBranchGrantResponse>>();
        Assert.DoesNotContain(afterRevoke!, g => g.BranchId == branchId);
    }

    [Fact]
    public async Task InviteUser_ThenAcceptInvite_ThenLoginWithNewPassword_FullLifecycle()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "invitelifecycle");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var roles = await (await client.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
        var branchManagerRole = roles!.Single(r => r.Name == "BranchManager");
        var branchId = await CreateBranchAsync(client);
        var email = $"invitee-{Guid.NewGuid():N}@example.com";

        var inviteResponse = await client.PostAsJsonAsync("/api/v1/users/invite",
            new InviteUserRequest(email, "Invited Person", null, branchManagerRole.Id, branchId, null));
        Assert.Equal(HttpStatusCode.OK, inviteResponse.StatusCode);

        // Read the plaintext invite token back out of the persisted OutboxMessage's InviteUrl —
        // never exposed over HTTP (only its SHA-256 hash is stored), but this row IS the same
        // artifact NotificationDispatchService (Task 10) would consume in production, so reading
        // it here proves the full real chain rather than reaching into AuthService internals.
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outboxMessage = await dbContext.Set<OutboxMessage>().IgnoreQueryFilters()
            .Where(m => m.EventType == "user.invited")
            .OrderByDescending(m => m.CreatedAt)
            .FirstAsync(m => m.Payload.Contains(email));
        var payload = JsonDocument.Parse(outboxMessage.Payload).RootElement;
        var inviteUrl = payload.GetProperty("InviteUrl").GetString()!;
        var token = Uri.UnescapeDataString(inviteUrl.Split("token=")[1]);

        var acceptResponse = await client.PostAsJsonAsync("/api/v1/auth/accept-invite", new AcceptInviteRequest(token, "N3wP@ssword!"));
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);
        var acceptResult = await acceptResponse.Content.ReadFromJsonAsync<AuthResult>();

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "N3wP@ssword!"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        Assert.Equal(acceptResult!.UserId, loginResult!.UserId);
    }

    [Fact]
    public async Task DeactivateUser_ThenLoginFails_ThenReactivate_LoginSucceeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "deactivatelifecycle");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var roles = await (await client.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
        var counterStaffRole = roles!.Single(r => r.Name == "CounterStaff");
        var branchId = await CreateBranchAsync(client);
        var email = $"staff-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/v1/users/invite", new InviteUserRequest(email, "Staff Person", null, counterStaffRole.Id, branchId, null));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invitedUser = await dbContext.Set<Courier.Identity.Domain.User>().IgnoreQueryFilters().SingleAsync(u => u.Email == email);
        var invite = await dbContext.Set<Courier.Identity.Domain.UserInvite>().SingleAsync(i => i.UserId == invitedUser.Id);
        var outboxMessage = await dbContext.Set<OutboxMessage>().IgnoreQueryFilters().Where(m => m.EventType == "user.invited").OrderByDescending(m => m.CreatedAt).FirstAsync(m => m.Payload.Contains(email));
        var token = Uri.UnescapeDataString(JsonDocument.Parse(outboxMessage.Payload).RootElement.GetProperty("InviteUrl").GetString()!.Split("token=")[1]);
        await client.PostAsJsonAsync("/api/v1/auth/accept-invite", new AcceptInviteRequest(token, "St@ffPassword1"));

        var deactivateResponse = await client.PatchAsync($"/api/v1/users/{invitedUser.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);
        // AuthEndpoints' /login returns 401 (not 400) for every AuthenticationFailedException,
        // including a deactivated account — matching LoginAsync's own generic invalid-credentials
        // treatment (no distinction that would let a caller enumerate account status).
        var loginAfterDeactivate = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "St@ffPassword1"));
        Assert.Equal(HttpStatusCode.Unauthorized, loginAfterDeactivate.StatusCode);

        var reactivateResponse = await client.PatchAsync($"/api/v1/users/{invitedUser.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, reactivateResponse.StatusCode);
        var loginAfterReactivate = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "St@ffPassword1"));
        Assert.Equal(HttpStatusCode.OK, loginAfterReactivate.StatusCode);
    }

    // Minimal real Branch, matching the shape SeedBranchesAsync-style helpers in the other
    // *EndpointsTests.cs files already use — this file only needs one branch, not an
    // origin/destination pair.
    private static async Task<Guid> CreateBranchAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var response = await client.PostAsJsonAsync("/api/v1/branches", new
        {
            code = $"BR{suffix}",
            name = $"Branch {suffix}",
            type = "Branch",
            provinceId = GeographyIds.ProvinceId("Bagmati"),
            districtId = GeographyIds.DistrictId("Bagmati", "Kathmandu"),
            localLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City"),
            wardNumber = 1,
            addressLine = "Somewhere in Kathmandu"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }
}
