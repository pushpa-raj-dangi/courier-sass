using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

public class BranchAuthorizationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BranchAuthorizationEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Scoped Org {suffix}", $"scoped-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task GetBranch_ScopedUser_ForbiddenWithoutGrant_ThenAllowedAfterParentGrant()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);

        var parentResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "PAR-01", "Parent Hub", BranchType.Hub, "Somewhere", null, null, null, null, null));
        var parent = await parentResponse.Content.ReadFromJsonAsync<BranchResponse>();

        var childResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "CHD-01", "Child Branch", BranchType.Branch, "Somewhere Else", null, null, null, null, parent!.Id));
        var child = await childResponse.Content.ReadFromJsonAsync<BranchResponse>();

        // No grant/user-management endpoint exists yet this phase, so the scoped test user's
        // role and UserBranch grant are seeded directly against AppDbContext, exactly like a
        // future "invite user" flow would persist them.
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var permission = await dbContext.Set<Permission>().FirstAsync(p => p.Key == "branch.read");
        var role = new Role { Id = Guid.NewGuid(), OrganizationId = owner.OrganizationId, Name = "ScopedReader", IsSystemDefault = false };
        role.Permissions.Add(permission);
        dbContext.Set<Role>().Add(role);

        var scopedUserId = Guid.NewGuid();
        dbContext.Set<User>().Add(new User
        {
            Id = scopedUserId,
            OrganizationId = owner.OrganizationId,
            Email = $"scoped-{scopedUserId:N}@example.com",
            PasswordHash = "not-used-in-this-test",
            FullName = "Scoped Reader"
        });
        dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = scopedUserId,
            RoleId = role.Id,
            OrganizationId = owner.OrganizationId,
            BranchScope = BranchScope.SpecificBranches,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = owner.UserId
        });
        await dbContext.SaveChangesAsync();

        var scopedToken = jwtTokenService.IssueAccessToken(scopedUserId, owner.OrganizationId, $"scoped-{scopedUserId:N}@example.com");
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scopedToken);

        var forbiddenResponse = await scopedClient.GetAsync($"/api/v1/branches/{child.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(),
            UserId = scopedUserId,
            BranchId = parent.Id,
            OrganizationId = owner.OrganizationId,
            GrantedAt = DateTimeOffset.UtcNow,
            GrantedByUserId = owner.UserId
        });
        await dbContext.SaveChangesAsync();

        var allowedResponse = await scopedClient.GetAsync($"/api/v1/branches/{child.Id}");
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
    }
}
