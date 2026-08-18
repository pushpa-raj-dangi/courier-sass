using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Branches.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Branches.Tests;

public class BranchHierarchyServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new BranchConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    private static Branch NewBranch(Guid id, Guid? parentId = null) => new()
    {
        Id = id,
        OrganizationId = Guid.NewGuid(),
        Code = id.ToString("N")[..8],
        Name = "Test Branch",
        Type = BranchType.Branch,
        AddressLine = "Somewhere",
        ParentBranchId = parentId
    };

    [Fact]
    public async Task GetDescendantBranchIdsAsync_RootWithDescendants_ReturnsSelfAndAllDescendants()
    {
        await using var dbContext = CreateContext();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var grandchildId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();

        dbContext.Set<Branch>().AddRange(
            NewBranch(rootId),
            NewBranch(childId, rootId),
            NewBranch(grandchildId, childId),
            NewBranch(unrelatedId));
        await dbContext.SaveChangesAsync();

        var service = new BranchHierarchyService(dbContext);
        var result = await service.GetDescendantBranchIdsAsync(rootId);

        Assert.Equal(new HashSet<Guid> { rootId, childId, grandchildId }, result);
    }

    [Fact]
    public async Task GetDescendantBranchIdsAsync_MidLevelBranch_ExcludesAncestors()
    {
        await using var dbContext = CreateContext();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var grandchildId = Guid.NewGuid();

        dbContext.Set<Branch>().AddRange(
            NewBranch(rootId),
            NewBranch(childId, rootId),
            NewBranch(grandchildId, childId));
        await dbContext.SaveChangesAsync();

        var service = new BranchHierarchyService(dbContext);
        var result = await service.GetDescendantBranchIdsAsync(childId);

        Assert.Equal(new HashSet<Guid> { childId, grandchildId }, result);
    }

    [Fact]
    public async Task GetDescendantBranchIdsAsync_LeafBranch_ReturnsOnlySelf()
    {
        await using var dbContext = CreateContext();
        var leafId = Guid.NewGuid();
        dbContext.Set<Branch>().Add(NewBranch(leafId));
        await dbContext.SaveChangesAsync();

        var service = new BranchHierarchyService(dbContext);
        var result = await service.GetDescendantBranchIdsAsync(leafId);

        Assert.Equal(new HashSet<Guid> { leafId }, result);
    }
}
