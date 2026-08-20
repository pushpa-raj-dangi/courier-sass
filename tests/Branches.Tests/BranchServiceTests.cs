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

        var service = new BranchService(dbContext, new FixedTenantProvider(branch.OrganizationId), new NoopAuditLogger(), new AllBranchesAuthorizationChecker(), new GeographyQueryService(dbContext));

        var result = await service.GetZoneInfoAsync(branch.Id);

        Assert.NotNull(result);
        Assert.Equal(zoneId, result!.DeliveryZoneId);
    }

    [Fact]
    public async Task GetZoneInfoAsync_UnknownBranch_ReturnsNull()
    {
        await using var dbContext = CreateSeededContext();
        var service = new BranchService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new AllBranchesAuthorizationChecker(), new GeographyQueryService(dbContext));

        Assert.Null(await service.GetZoneInfoAsync(Guid.NewGuid()));
    }
}
