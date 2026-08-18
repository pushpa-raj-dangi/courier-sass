using System.Security.Claims;
using Courier.Branches.Domain;
using Courier.Branches.Infrastructure;
using Courier.Geography.Infrastructure;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Identity.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

public class BranchAuthorizationCheckerTests
{
    // This test uses the real Postgres-backed AppDbContext (TestDbContextFactory), not an
    // in-memory provider, so Branch's geography FKs are actually enforced — arbitrary Guids
    // would violate the FK constraints added in this task. Point at the seeded Kathmandu chain.
    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private static async Task<Guid> SeedUserAsync(AppDbContext dbContext, Guid organizationId, string permissionKey, BranchScope branchScope)
    {
        dbContext.Set<Organization>().Add(new Organization
        {
            Id = organizationId,
            Name = $"Test Org {organizationId:N}",
            Slug = $"test-org-{organizationId:N}"
        });

        var permission = await dbContext.Set<Permission>().FirstAsync(p => p.Key == permissionKey);
        var role = new Role { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "TestRole", IsSystemDefault = false };
        role.Permissions.Add(permission);
        dbContext.Set<Role>().Add(role);

        var userId = Guid.NewGuid();
        dbContext.Set<User>().Add(new User
        {
            Id = userId,
            OrganizationId = organizationId,
            Email = $"user-{userId:N}@example.com",
            PasswordHash = "not-used-in-this-test",
            FullName = "Test User"
        });

        dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = role.Id,
            OrganizationId = organizationId,
            BranchScope = branchScope,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = userId
        });

        await dbContext.SaveChangesAsync();
        return userId;
    }

    private static Branch NewBranch(Guid organizationId, Guid id, Guid? parentId = null) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        Code = id.ToString("N")[..8],
        Name = "Test Branch",
        Type = BranchType.Branch,
        ProvinceId = KathmanduProvinceId,
        DistrictId = KathmanduDistrictId,
        LocalLevelId = KathmanduLocalLevelId,
        WardNumber = 1,
        AddressLine = "Somewhere in Kathmandu",
        ParentBranchId = parentId
    };

    private static ClaimsPrincipal BuildPrincipal(Guid userId)
        => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "TestAuth"));

    private static BranchAuthorizationChecker CreateChecker(AppDbContext dbContext)
        => new(dbContext, new BranchHierarchyService(dbContext));

    [Fact]
    public async Task HasAccessAsync_SingleBranchGrant_AllowsGrantedBranchOnly()
    {
        var organizationId = Guid.NewGuid();
        await using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var userId = await SeedUserAsync(dbContext, organizationId, "branch.read", BranchScope.SpecificBranches);

        var grantedBranchId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();
        dbContext.Set<Branch>().AddRange(
            NewBranch(organizationId, grantedBranchId),
            NewBranch(organizationId, otherBranchId));
        dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(), UserId = userId, BranchId = grantedBranchId, OrganizationId = organizationId,
            GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = userId
        });
        await dbContext.SaveChangesAsync();

        var checker = CreateChecker(dbContext);
        var principal = BuildPrincipal(userId);

        Assert.True(await checker.HasAccessAsync(principal, "branch.read", grantedBranchId));
        Assert.False(await checker.HasAccessAsync(principal, "branch.read", otherBranchId));
    }

    [Fact]
    public async Task HasAccessAsync_MultiBranchGrant_AllowsAllGrantedBranches()
    {
        var organizationId = Guid.NewGuid();
        await using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var userId = await SeedUserAsync(dbContext, organizationId, "branch.read", BranchScope.SpecificBranches);

        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var ungranted = Guid.NewGuid();
        dbContext.Set<Branch>().AddRange(
            NewBranch(organizationId, branchA),
            NewBranch(organizationId, branchB),
            NewBranch(organizationId, ungranted));
        dbContext.Set<UserBranch>().AddRange(
            new UserBranch { Id = Guid.NewGuid(), UserId = userId, BranchId = branchA, OrganizationId = organizationId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = userId },
            new UserBranch { Id = Guid.NewGuid(), UserId = userId, BranchId = branchB, OrganizationId = organizationId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = userId });
        await dbContext.SaveChangesAsync();

        var checker = CreateChecker(dbContext);
        var principal = BuildPrincipal(userId);

        Assert.True(await checker.HasAccessAsync(principal, "branch.read", branchA));
        Assert.True(await checker.HasAccessAsync(principal, "branch.read", branchB));
        Assert.False(await checker.HasAccessAsync(principal, "branch.read", ungranted));
    }

    [Fact]
    public async Task GetAccessibleBranchIdsAsync_AllBranchesScope_ReturnsNullAndAllowsAnyBranch()
    {
        var organizationId = Guid.NewGuid();
        await using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var userId = await SeedUserAsync(dbContext, organizationId, "branch.read", BranchScope.AllBranches);

        var someBranchId = Guid.NewGuid();
        dbContext.Set<Branch>().Add(NewBranch(organizationId, someBranchId));
        await dbContext.SaveChangesAsync();

        var checker = CreateChecker(dbContext);
        var principal = BuildPrincipal(userId);

        var accessible = await checker.GetAccessibleBranchIdsAsync(principal, "branch.read");
        Assert.Null(accessible);
        Assert.True(await checker.HasAccessAsync(principal, "branch.read", someBranchId));
    }

    [Fact]
    public async Task HasAccessAsync_ParentBranchGrant_CoversDescendantBranches()
    {
        var organizationId = Guid.NewGuid();
        await using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var userId = await SeedUserAsync(dbContext, organizationId, "branch.read", BranchScope.SpecificBranches);

        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();
        dbContext.Set<Branch>().AddRange(
            NewBranch(organizationId, parentId),
            NewBranch(organizationId, childId, parentId),
            NewBranch(organizationId, unrelatedId));
        dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(), UserId = userId, BranchId = parentId, OrganizationId = organizationId,
            GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = userId
        });
        await dbContext.SaveChangesAsync();

        var checker = CreateChecker(dbContext);
        var principal = BuildPrincipal(userId);

        Assert.True(await checker.HasAccessAsync(principal, "branch.read", childId));
        Assert.False(await checker.HasAccessAsync(principal, "branch.read", unrelatedId));
    }

    [Fact]
    public async Task HasAccessAsync_ExpiredGrant_IsRejected()
    {
        var organizationId = Guid.NewGuid();
        await using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var userId = await SeedUserAsync(dbContext, organizationId, "branch.read", BranchScope.SpecificBranches);

        var branchId = Guid.NewGuid();
        dbContext.Set<Branch>().Add(NewBranch(organizationId, branchId));
        dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(), UserId = userId, BranchId = branchId, OrganizationId = organizationId,
            GrantedAt = DateTimeOffset.UtcNow.AddDays(-10), ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            GrantedByUserId = userId
        });
        await dbContext.SaveChangesAsync();

        var checker = CreateChecker(dbContext);
        var principal = BuildPrincipal(userId);

        Assert.False(await checker.HasAccessAsync(principal, "branch.read", branchId));
    }

    [Fact]
    public async Task HasAccessAsync_ExpiredAndValidGrants_FiltersPerRow()
    {
        var organizationId = Guid.NewGuid();
        await using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var userId = await SeedUserAsync(dbContext, organizationId, "branch.read", BranchScope.SpecificBranches);

        var expiredBranchId = Guid.NewGuid();
        var validBranchId = Guid.NewGuid();
        dbContext.Set<Branch>().AddRange(
            NewBranch(organizationId, expiredBranchId),
            NewBranch(organizationId, validBranchId));
        dbContext.Set<UserBranch>().AddRange(
            new UserBranch
            {
                Id = Guid.NewGuid(), UserId = userId, BranchId = expiredBranchId, OrganizationId = organizationId,
                GrantedAt = DateTimeOffset.UtcNow.AddDays(-10), ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
                GrantedByUserId = userId
            },
            new UserBranch
            {
                Id = Guid.NewGuid(), UserId = userId, BranchId = validBranchId, OrganizationId = organizationId,
                GrantedAt = DateTimeOffset.UtcNow.AddDays(-10), ExpiresAt = null,
                GrantedByUserId = userId
            });
        await dbContext.SaveChangesAsync();

        var checker = CreateChecker(dbContext);
        var principal = BuildPrincipal(userId);

        // Proves the ExpiresAt filter applies per grant row, not as an all-or-nothing gate.
        Assert.False(await checker.HasAccessAsync(principal, "branch.read", expiredBranchId));
        Assert.True(await checker.HasAccessAsync(principal, "branch.read", validBranchId));
    }
}
