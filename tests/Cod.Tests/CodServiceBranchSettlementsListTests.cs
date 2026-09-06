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

file class RestrictedBranchAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid> _accessibleBranchIds;
    public RestrictedBranchAuthorizationChecker(params Guid[] accessibleBranchIds) => _accessibleBranchIds = accessibleBranchIds.ToHashSet();
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(_accessibleBranchIds);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessibleBranchIds.Contains(branchId));
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

public class CodServiceBranchSettlementsListTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new BranchCashSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new RiderCashSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new FinancialTransactionConfiguration());
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

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId, IBranchAuthorizationChecker branchAuthorizationChecker) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        branchAuthorizationChecker, new AlwaysTrueMerchantLookup(), new AlwaysTrueShipmentAwbLookup(), new NoopOutboxWriter());

    private static async Task<BranchCashSettlement> SeedAsync(TestDbContext dbContext, Guid organizationId, Guid branchId)
    {
        var settlement = new BranchCashSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, BranchId = branchId,
            ConfirmedAmount = 2500m, ReconciledByUserId = Guid.NewGuid(), ReconciledAt = DateTimeOffset.UtcNow
        };
        dbContext.Set<BranchCashSettlement>().Add(settlement);
        await dbContext.SaveChangesAsync();
        return settlement;
    }

    private static async Task VerifyAsync(TestDbContext dbContext, Guid organizationId, Guid branchCashSettlementId)
    {
        dbContext.Set<FinancialTransaction>().Add(new FinancialTransaction
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Type = FinancialTransactionType.CodVerification,
            Amount = 2500m, ReferenceEntityType = "BranchCashSettlement", ReferenceEntityId = branchCashSettlementId,
            VerifiedByUserId = Guid.NewGuid(), VerifiedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task ListBranchSettlementsAsync_ExcludesForeignOrganization()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        var own = await SeedAsync(dbContext, organizationId, Guid.NewGuid());
        await SeedAsync(dbContext, foreignOrganizationId, Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListBranchSettlementsAsync(null, null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(own.Id, single.Id);
    }

    [Fact]
    public async Task ListBranchSettlementsAsync_BranchRestricted_FiltersToAccessibleBranchOnly()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var accessibleBranchId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();
        var accessible = await SeedAsync(dbContext, organizationId, accessibleBranchId);
        await SeedAsync(dbContext, organizationId, otherBranchId);
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker(accessibleBranchId));

        var result = await service.ListBranchSettlementsAsync(null, null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(accessible.Id, single.Id);
    }

    [Fact]
    public async Task ListBranchSettlementsAsync_UnverifiedOnly_ExcludesFinanceVerifiedRows()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var unverified = await SeedAsync(dbContext, organizationId, Guid.NewGuid());
        var verified = await SeedAsync(dbContext, organizationId, Guid.NewGuid());
        await VerifyAsync(dbContext, organizationId, verified.Id);
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListBranchSettlementsAsync(null, true, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(unverified.Id, single.Id);
    }

    [Fact]
    public async Task ListBranchSettlementsAsync_IncludesCoveredRiderCashSettlementIds()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchSettlement = await SeedAsync(dbContext, organizationId, Guid.NewGuid());
        var riderSettlement = new RiderCashSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, RiderId = Guid.NewGuid(), BranchId = branchSettlement.BranchId,
            DeclaredAmount = 2500m, ShipmentCount = 1, SettledAt = DateTimeOffset.UtcNow,
            ConfirmedByUserId = Guid.NewGuid(), BranchCashSettlementId = branchSettlement.Id
        };
        dbContext.Set<RiderCashSettlement>().Add(riderSettlement);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListBranchSettlementsAsync(null, null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(riderSettlement.Id, Assert.Single(single.RiderCashSettlementIds));
    }
}
