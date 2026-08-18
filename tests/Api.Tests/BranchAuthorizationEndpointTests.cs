using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Infrastructure;
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

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

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

    // Same direct-seeding approach as GetBranch_ScopedUser_...: no grant/user-management endpoint
    // exists yet this phase, so the scoped test user's role, UserRole scope and UserBranch grants
    // are persisted straight against AppDbContext, exactly like a future "invite user" flow would.
    private async Task<string> SeedScopedUserTokenAsync(
        Guid organizationId, Guid ownerUserId, string permissionKey, params Guid[] grantedBranchIds)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var permission = await dbContext.Set<Permission>().FirstAsync(p => p.Key == permissionKey);
        var role = new Role
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = $"ScopedRole-{permissionKey}",
            IsSystemDefault = false
        };
        role.Permissions.Add(permission);
        dbContext.Set<Role>().Add(role);

        var scopedUserId = Guid.NewGuid();
        var email = $"scoped-{scopedUserId:N}@example.com";
        dbContext.Set<User>().Add(new User
        {
            Id = scopedUserId,
            OrganizationId = organizationId,
            Email = email,
            PasswordHash = "not-used-in-this-test",
            FullName = "Scoped User"
        });
        dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = scopedUserId,
            RoleId = role.Id,
            OrganizationId = organizationId,
            BranchScope = BranchScope.SpecificBranches,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = ownerUserId
        });
        foreach (var branchId in grantedBranchIds)
        {
            dbContext.Set<UserBranch>().Add(new UserBranch
            {
                Id = Guid.NewGuid(),
                UserId = scopedUserId,
                BranchId = branchId,
                OrganizationId = organizationId,
                GrantedAt = DateTimeOffset.UtcNow,
                GrantedByUserId = ownerUserId
            });
        }
        await dbContext.SaveChangesAsync();

        return jwtTokenService.IssueAccessToken(scopedUserId, organizationId, email);
    }

    [Fact]
    public async Task ListBranches_ScopedUser_ReturnsOnlyGrantedBranches()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);

        var grantedResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "LSG-01", "Granted Branch", BranchType.Branch,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Somewhere", null, null, null, null, null));
        grantedResponse.EnsureSuccessStatusCode();
        var granted = (await grantedResponse.Content.ReadFromJsonAsync<BranchResponse>())!;

        var ungrantedResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "LSU-01", "Ungranted Branch", BranchType.Branch,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Elsewhere", null, null, null, null, null));
        ungrantedResponse.EnsureSuccessStatusCode();

        var scopedToken = await SeedScopedUserTokenAsync(owner.OrganizationId, owner.UserId, "branch.read", granted.Id);
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scopedToken);

        var listResponse = await scopedClient.GetAsync("/api/v1/branches");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var branches = (await listResponse.Content.ReadFromJsonAsync<List<BranchResponse>>())!;
        Assert.Contains(branches, b => b.Code == "LSG-01");
        Assert.DoesNotContain(branches, b => b.Code == "LSU-01");
    }

    [Fact]
    public async Task CreateBranch_ScopedUser_OutOfScopeParentBranch_Returns400()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);

        var grantedResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "CRG-01", "Granted Branch", BranchType.Branch,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Somewhere", null, null, null, null, null));
        grantedResponse.EnsureSuccessStatusCode();
        var granted = (await grantedResponse.Content.ReadFromJsonAsync<BranchResponse>())!;

        var outOfScopeResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "CRO-01", "Out Of Scope Parent", BranchType.Hub,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Elsewhere", null, null, null, null, null));
        outOfScopeResponse.EnsureSuccessStatusCode();
        var outOfScopeParent = (await outOfScopeResponse.Content.ReadFromJsonAsync<BranchResponse>())!;

        var scopedToken = await SeedScopedUserTokenAsync(owner.OrganizationId, owner.UserId, "branch.create", granted.Id);
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scopedToken);

        var response = await scopedClient.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "CRN-01", "Attempted Child", BranchType.Branch,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Nowhere", null, null, null, null, outOfScopeParent.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // Pin the reason: a 400 here must be the branch-scope rejection, not an unrelated
        // validation failure such as a duplicate branch code.
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("outside your branch access", body);
    }

    [Fact]
    public async Task GetBranch_ScopedUser_ForbiddenWithoutGrant_ThenAllowedAfterParentGrant()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);

        var parentResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "PAR-01", "Parent Hub", BranchType.Hub,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Somewhere", null, null, null, null, null));
        var parent = await parentResponse.Content.ReadFromJsonAsync<BranchResponse>();

        var childResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "CHD-01", "Child Branch", BranchType.Branch,
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
            "Somewhere Else", null, null, null, null, parent!.Id));
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
