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

file class FakeBranchPlanLimitProvider : IBranchPlanLimitProvider
{
    public int? MaxBranches { get; set; }
    public Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(MaxBranches);
}

public class BranchServiceReportingTests
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
    public async Task GetNamesByIdsAsync_ReturnsNameForEachRequestedBranch()
    {
        // Arrange: seed two Branch rows for the current org via the InMemory context.
        await using var dbContext = CreateSeededContext();
        var organizationId = Guid.NewGuid();
        var branch1 = new Branch
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Code = "KTM-01", Name = "Kathmandu Hub",
            Type = BranchType.Hub, ProvinceId = Guid.NewGuid(), DistrictId = Guid.NewGuid(),
            LocalLevelId = Guid.NewGuid(), WardNumber = 1, AddressLine = "New Road",
            DeliveryZoneId = null, IsActive = true
        };
        var branch2 = new Branch
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Code = "KTM-02", Name = "Lalitpur Branch",
            Type = BranchType.Branch, ProvinceId = Guid.NewGuid(), DistrictId = Guid.NewGuid(),
            LocalLevelId = Guid.NewGuid(), WardNumber = 2, AddressLine = "Ring Road",
            DeliveryZoneId = null, IsActive = true
        };
        dbContext.Set<Branch>().Add(branch1);
        dbContext.Set<Branch>().Add(branch2);
        await dbContext.SaveChangesAsync();

        var service = new BranchService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AllBranchesAuthorizationChecker(), new GeographyQueryService(dbContext), new FakeBranchPlanLimitProvider { MaxBranches = null });

        // Act: call service.GetNamesByIdsAsync(new HashSet<Guid> { branch1.Id, branch2.Id });
        var result = await service.GetNamesByIdsAsync(new HashSet<Guid> { branch1.Id, branch2.Id });

        // Assert: dictionary has 2 entries, keyed by Id, valued by Name.
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(branch1.Name, result[branch1.Id]);
        Assert.Equal(branch2.Name, result[branch2.Id]);
    }

    [Fact]
    public async Task GetNamesByIdsAsync_EmptySet_ReturnsEmptyDictionary()
    {
        // Arrange
        await using var dbContext = CreateSeededContext();
        var service = new BranchService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new AllBranchesAuthorizationChecker(), new GeographyQueryService(dbContext), new FakeBranchPlanLimitProvider { MaxBranches = null });

        // Act: call with an empty HashSet<Guid>.
        var result = await service.GetNamesByIdsAsync(new HashSet<Guid>());

        // Assert: result.Count == 0, and no DB round trip is required for correctness (behavior, not
        // an implementation detail — just assert the empty result).
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSummaryAsync_CountsActiveInactiveAndByType()
    {
        await using var dbContext = CreateSeededContext();
        var organizationId = Guid.NewGuid();
        var activeHub = new Branch
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Code = "KTM-01", Name = "Kathmandu Hub",
            Type = BranchType.Hub, ProvinceId = Guid.NewGuid(), DistrictId = Guid.NewGuid(),
            LocalLevelId = Guid.NewGuid(), WardNumber = 1, AddressLine = "New Road",
            DeliveryZoneId = null, IsActive = true
        };
        var inactiveBranch = new Branch
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Code = "KTM-02", Name = "Lalitpur Branch",
            Type = BranchType.Branch, ProvinceId = Guid.NewGuid(), DistrictId = Guid.NewGuid(),
            LocalLevelId = Guid.NewGuid(), WardNumber = 2, AddressLine = "Ring Road",
            DeliveryZoneId = null, IsActive = false
        };
        dbContext.Set<Branch>().Add(activeHub);
        dbContext.Set<Branch>().Add(inactiveBranch);
        await dbContext.SaveChangesAsync();

        var service = new BranchService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AllBranchesAuthorizationChecker(), new GeographyQueryService(dbContext), new FakeBranchPlanLimitProvider { MaxBranches = null });

        var result = await service.GetSummaryAsync(new ClaimsPrincipal());

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Active);
        Assert.Equal(1, result.Inactive);
        Assert.Equal(1, result.ByType[BranchType.Hub]);
        Assert.Equal(1, result.ByType[BranchType.Branch]);
    }
}
