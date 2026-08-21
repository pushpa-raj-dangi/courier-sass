using Courier.Branches.Domain;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Identity.Infrastructure;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courier.Identity.Tests;

file class NullTenantProvider : ICurrentTenantProvider
{
    public Guid? OrganizationId => null;
}

public class UserProvisioningServiceTests
{
    private static AppDbContext CreateContext() => TestDbContextFactory.Create(new NullTenantProvider());

    private static AuthService CreateAuthService(AppDbContext dbContext)
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            SigningKey = "identity-tests-signing-key-at-least-32-bytes!",
            Issuer = "courier-tests",
            Audience = "courier-tests"
        });
        return new AuthService(dbContext, new PasswordHasherAdapter(), new JwtTokenService(jwtOptions), new NoopAuditLogger());
    }

    private class NoopAuditLogger : Courier.Audit.Application.IAuditLogger
    {
        public Task LogAsync(Courier.Audit.Application.AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static async Task<(Guid OrganizationId, Guid OwnerUserId)> RegisterOrgAsync(AppDbContext dbContext)
    {
        var authService = CreateAuthService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var result = await authService.RegisterOrganizationAsync(new RegisterOrganizationRequest(
            $"Test Org {suffix}", $"test-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person"));
        return (result.OrganizationId, result.UserId);
    }

    // UserBranch.BranchId carries a real FK constraint to Branches (Phase 2) — a random Guid fails
    // at SaveChangesAsync, so this seeds a minimal real Branch row against the globally-seeded
    // Kathmandu geography reference data (GeographyIds), same ids Api.Tests uses.
    private static async Task<Guid> SeedBranchAsync(AppDbContext dbContext, Guid organizationId)
    {
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = $"BR-{Guid.NewGuid():N}"[..10],
            Name = "Test Branch",
            Type = BranchType.Branch,
            ProvinceId = GeographyIds.ProvinceId("Bagmati"),
            DistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu"),
            LocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City"),
            WardNumber = 1,
            AddressLine = "New Road, Kathmandu"
        };
        dbContext.Set<Branch>().Add(branch);
        await dbContext.SaveChangesAsync();
        return branch.Id;
    }

    [Fact]
    public async Task RegisterOrganizationAsync_SeedsPickupRiderAndDeliveryRiderRoles()
    {
        await using var dbContext = CreateContext();
        var (organizationId, _) = await RegisterOrgAsync(dbContext);

        var roleNames = dbContext.Set<Courier.Identity.Domain.Role>().IgnoreQueryFilters()
            .Where(r => r.OrganizationId == organizationId)
            .Select(r => r.Name)
            .ToList();

        Assert.Contains("PickupRider", roleNames);
        Assert.Contains("DeliveryRider", roleNames);
    }

    [Fact]
    public async Task ProvisionStaffUserAsync_ValidRole_CreatesUserWithRoleAndBranchGrant()
    {
        await using var dbContext = CreateContext();
        var (organizationId, ownerUserId) = await RegisterOrgAsync(dbContext);
        var branchId = await SeedBranchAsync(dbContext, organizationId);
        var service = new UserProvisioningService(dbContext, new PasswordHasherAdapter());
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var userId = await service.ProvisionStaffUserAsync(
            new ProvisionStaffUserRequest($"rider-{suffix}@example.com", "P@ssw0rd123!", "Test Rider", null, "DeliveryRider", branchId),
            organizationId, ownerUserId);

        var userBranch = dbContext.Set<Courier.Identity.Domain.UserBranch>().IgnoreQueryFilters().Single(ub => ub.UserId == userId);
        Assert.Equal(branchId, userBranch.BranchId);
        var userRole = dbContext.Set<Courier.Identity.Domain.UserRole>().IgnoreQueryFilters().Single(ur => ur.UserId == userId);
        Assert.Equal(Courier.Identity.Domain.BranchScope.SpecificBranches, userRole.BranchScope);
    }

    [Fact]
    public async Task ProvisionStaffUserAsync_UnknownRoleName_ThrowsUserProvisioningException()
    {
        await using var dbContext = CreateContext();
        var (organizationId, ownerUserId) = await RegisterOrgAsync(dbContext);
        var service = new UserProvisioningService(dbContext, new PasswordHasherAdapter());
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await Assert.ThrowsAsync<UserProvisioningException>(() => service.ProvisionStaffUserAsync(
            new ProvisionStaffUserRequest($"rider-{suffix}@example.com", "P@ssw0rd123!", "Test Rider", null, "NoSuchRole", Guid.NewGuid()),
            organizationId, ownerUserId));
    }

    [Fact]
    public async Task ProvisionStaffUserAsync_DuplicateEmail_ThrowsUserProvisioningException()
    {
        await using var dbContext = CreateContext();
        var (organizationId, ownerUserId) = await RegisterOrgAsync(dbContext);
        var branchId = await SeedBranchAsync(dbContext, organizationId);
        var service = new UserProvisioningService(dbContext, new PasswordHasherAdapter());
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"rider-{suffix}@example.com";
        await service.ProvisionStaffUserAsync(
            new ProvisionStaffUserRequest(email, "P@ssw0rd123!", "Test Rider", null, "DeliveryRider", branchId),
            organizationId, ownerUserId);

        await Assert.ThrowsAsync<UserProvisioningException>(() => service.ProvisionStaffUserAsync(
            new ProvisionStaffUserRequest(email, "P@ssw0rd123!", "Another Rider", null, "PickupRider", branchId),
            organizationId, ownerUserId));
    }
}
