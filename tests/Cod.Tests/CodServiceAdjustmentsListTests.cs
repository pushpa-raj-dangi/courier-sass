using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
using Courier.Events.Application;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Cod.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class AlwaysTrueBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class AlwaysTrueShipmentAwbLookup : IShipmentAwbLookupService
{
    public Task<IReadOnlyDictionary<Guid, string>> GetAwbsByShipmentIdsAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class NoopOutboxWriter : IOutboxWriter
{
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class CodServiceAdjustmentsListTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new AdjustmentConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        new AllBranchesAuthorizationChecker(), new AlwaysTrueMerchantLookup(), new AlwaysTrueShipmentAwbLookup(), new NoopOutboxWriter());

    private static async Task<Adjustment> SeedAsync(TestDbContext dbContext, Guid organizationId, bool applied)
    {
        var adjustment = new Adjustment
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, EntityType = "MerchantSettlementItem", EntityId = Guid.NewGuid(),
            BeforeValue = 0m, AfterValue = 100m, Reason = "Damaged parcel deduction",
            RequestedByUserId = Guid.NewGuid(), RequestedAt = DateTimeOffset.UtcNow,
            ApprovedByUserId = applied ? Guid.NewGuid() : null, ApprovedAt = applied ? DateTimeOffset.UtcNow : null, Applied = applied
        };
        dbContext.Set<Adjustment>().Add(adjustment);
        await dbContext.SaveChangesAsync();
        return adjustment;
    }

    [Fact]
    public async Task ListAdjustmentsAsync_ExcludesForeignOrganization()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        var own = await SeedAsync(dbContext, organizationId, applied: false);
        await SeedAsync(dbContext, foreignOrganizationId, applied: false);
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListAdjustmentsAsync(null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(own.Id, single.Id);
    }

    [Fact]
    public async Task ListAdjustmentsAsync_FilteredByAppliedFalse_ReturnsOnlyPending()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var pending = await SeedAsync(dbContext, organizationId, applied: false);
        await SeedAsync(dbContext, organizationId, applied: true);
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListAdjustmentsAsync(false, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(pending.Id, single.Id);
    }

    [Fact]
    public async Task ListAdjustmentsAsync_FilteredByAppliedTrue_ReturnsOnlyApplied()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        await SeedAsync(dbContext, organizationId, applied: false);
        var applied = await SeedAsync(dbContext, organizationId, applied: true);
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListAdjustmentsAsync(true, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(applied.Id, single.Id);
    }

    [Fact]
    public async Task ListAdjustmentsAsync_NoFilter_ReturnsBoth()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        await SeedAsync(dbContext, organizationId, applied: false);
        await SeedAsync(dbContext, organizationId, applied: true);
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListAdjustmentsAsync(null, null, null, ActingUser(Guid.NewGuid()));

        Assert.Equal(2, result.TotalCount);
    }
}
