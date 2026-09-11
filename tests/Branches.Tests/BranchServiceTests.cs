using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Branches.Infrastructure;
using Courier.Geography.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace Courier.Branches.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

// Always-valid geography stub — used for tests that exercise CreateBranchAsync and need to get
// past geography validation without registering Geography's entity types (Ward/LocalLevel/etc.)
// into the minimal TestDbContext model below, which only configures Branch.
file class AllowAllGeographyLookupService : IGeographyLookupService
{
    public Task<bool> IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid localLevelId, int wardNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
    public Task<bool> DeliveryZoneExistsAsync(Guid zoneId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class FakeBranchPlanLimitProvider : IBranchPlanLimitProvider
{
    public int? MaxBranches { get; set; }
    public Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(MaxBranches);
}

public class BranchServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new BranchConfiguration());
        }
    }

    private static TestDbContext CreateSeededContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    [Fact]
    public async Task GetZoneInfoAsync_KnownBranch_ReturnsDeliveryZoneId()
    {
        await using var dbContext = CreateSeededContext();
        var zoneId = Guid.NewGuid();
        var branch = new Branch
        {
            Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), Code = "KTM-01", Name = "Kathmandu Hub",
            Type = BranchType.Hub, ProvinceId = Guid.NewGuid(), DistrictId = Guid.NewGuid(),
            LocalLevelId = Guid.NewGuid(), WardNumber = 1, AddressLine = "New Road",
            DeliveryZoneId = zoneId, IsActive = true
        };
        dbContext.Set<Branch>().Add(branch);
        await dbContext.SaveChangesAsync();

        var service = new BranchService(dbContext, new FixedTenantProvider(branch.OrganizationId), new NoopAuditLogger(), new AllBranchesAuthorizationChecker(), new GeographyQueryService(dbContext), new FakeBranchPlanLimitProvider { MaxBranches = null });

        var result = await service.GetZoneInfoAsync(branch.Id);

        Assert.NotNull(result);
        Assert.Equal(zoneId, result!.DeliveryZoneId);
    }

    [Fact]
    public async Task GetZoneInfoAsync_UnknownBranch_ReturnsNull()
    {
        await using var dbContext = CreateSeededContext();
        var service = new BranchService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new AllBranchesAuthorizationChecker(), new GeographyQueryService(dbContext), new FakeBranchPlanLimitProvider { MaxBranches = null });

        Assert.Null(await service.GetZoneInfoAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListBranchesForMerchantAsync_ReturnsOnlyActiveBranches_NoAccessNarrowing()
    {
        await using var dbContext = CreateSeededContext();
        var orgId = Guid.NewGuid();
        var active = new Branch
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, Code = "KTM-01", Name = "Kathmandu Hub",
            Type = BranchType.Hub, ProvinceId = Guid.NewGuid(), DistrictId = Guid.NewGuid(),
            LocalLevelId = Guid.NewGuid(), WardNumber = 1, AddressLine = "New Road", IsActive = true
        };
        var inactive = new Branch
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, Code = "KTM-02", Name = "Closed Branch",
            Type = BranchType.Branch, ProvinceId = Guid.NewGuid(), DistrictId = Guid.NewGuid(),
            LocalLevelId = Guid.NewGuid(), WardNumber = 2, AddressLine = "Old Road", IsActive = false
        };
        dbContext.Set<Branch>().AddRange(active, inactive);
        await dbContext.SaveChangesAsync();
        var service = new BranchService(dbContext, new FixedTenantProvider(orgId), new NoopAuditLogger(), new AllBranchesAuthorizationChecker(), new GeographyQueryService(dbContext), new FakeBranchPlanLimitProvider { MaxBranches = null });

        var result = await service.ListBranchesForMerchantAsync();

        var single = Assert.Single(result);
        Assert.Equal(active.Id, single.Id);
    }

    private static CreateBranchRequest ValidCreateRequest(string code) => new(
        code, $"Branch {code}", BranchType.Branch,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
        "Somewhere", null, null, null, null, null);

    [Fact]
    public async Task CreateBranchAsync_AtPlanLimit_ThrowsBranchLimitExceeded()
    {
        await using var dbContext = CreateSeededContext();
        var organizationId = Guid.NewGuid();
        var existing = new Branch
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Code = "EXIST-01", Name = "Existing Branch",
            Type = BranchType.Branch, ProvinceId = Guid.NewGuid(), DistrictId = Guid.NewGuid(),
            LocalLevelId = Guid.NewGuid(), WardNumber = 1, AddressLine = "Somewhere", IsActive = true
        };
        dbContext.Set<Branch>().Add(existing);
        await dbContext.SaveChangesAsync();

        var service = new BranchService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AllBranchesAuthorizationChecker(), new AllowAllGeographyLookupService(), new FakeBranchPlanLimitProvider { MaxBranches = 1 });

        await Assert.ThrowsAsync<BranchLimitExceededException>(() =>
            service.CreateBranchAsync(ValidCreateRequest("NEW-01"), new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "TestAuth"))));
    }

    [Fact]
    public async Task CreateBranchAsync_UnlimitedPlan_Succeeds()
    {
        await using var dbContext = CreateSeededContext();
        var organizationId = Guid.NewGuid();
        var existing = new Branch
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Code = "EXIST-01", Name = "Existing Branch",
            Type = BranchType.Branch, ProvinceId = Guid.NewGuid(), DistrictId = Guid.NewGuid(),
            LocalLevelId = Guid.NewGuid(), WardNumber = 1, AddressLine = "Somewhere", IsActive = true
        };
        dbContext.Set<Branch>().Add(existing);
        await dbContext.SaveChangesAsync();

        var service = new BranchService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AllBranchesAuthorizationChecker(), new AllowAllGeographyLookupService(), new FakeBranchPlanLimitProvider { MaxBranches = null });

        var result = await service.CreateBranchAsync(ValidCreateRequest("NEW-02"), new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "TestAuth")));

        Assert.NotNull(result);
        Assert.Equal("NEW-02", result.Code);
    }

    [Fact]
    public async Task ListForOrganizationAsync_ScopesToOrganizationAndPaginates()
    {
        await using var dbContext = CreateSeededContext();
        var organizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            dbContext.Set<Branch>().Add(new Branch
            {
                Id = Guid.NewGuid(), OrganizationId = organizationId, Code = $"BR-{i}", Name = $"Branch {i}",
                Type = BranchType.Branch, ProvinceId = Guid.NewGuid(), DistrictId = Guid.NewGuid(),
                LocalLevelId = Guid.NewGuid(), WardNumber = 1, AddressLine = "Somewhere", IsActive = true
            });
        }
        dbContext.Set<Branch>().Add(new Branch
        {
            Id = Guid.NewGuid(), OrganizationId = otherOrganizationId, Code = "OTHER-01", Name = "Other Org Branch",
            Type = BranchType.Branch, ProvinceId = Guid.NewGuid(), DistrictId = Guid.NewGuid(),
            LocalLevelId = Guid.NewGuid(), WardNumber = 1, AddressLine = "Somewhere", IsActive = true
        });
        await dbContext.SaveChangesAsync();
        var service = new BranchService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AllBranchesAuthorizationChecker(), new AllowAllGeographyLookupService(), new FakeBranchPlanLimitProvider { MaxBranches = null });

        var page1 = await service.ListForOrganizationAsync(organizationId, page: 1, pageSize: 2);
        var page2 = await service.ListForOrganizationAsync(organizationId, page: 2, pageSize: 2);

        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        Assert.Equal(3, page1.TotalCount);
        Assert.DoesNotContain(page1.Items.Concat(page2.Items), b => b.Name == "Other Org Branch");
    }
}
