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
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
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

public class CodServiceReadsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantSettlementItemConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal StaffActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    private static ClaimsPrincipal MerchantActingUser(Guid merchantId) => new(new ClaimsIdentity(new[] { new Claim("merchant_id", merchantId.ToString()) }, "test"));

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId, IBranchAuthorizationChecker branchAuthorizationChecker) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        branchAuthorizationChecker, new AlwaysTrueMerchantLookup(), new AlwaysTrueShipmentAwbLookup(), new NoopOutboxWriter());

    private static async Task<CODCollection> SeedCollectionAsync(TestDbContext dbContext, Guid organizationId, Guid merchantId, Guid branchId, CodCollectionStatus status, decimal? collectedAmount)
    {
        var collection = new CODCollection
        {
            Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = organizationId, MerchantId = merchantId,
            BranchId = branchId, ExpectedAmount = 2500m, ShipmentTotalCharge = 150m, CollectedAmount = collectedAmount,
            CollectedAt = collectedAmount is null ? null : DateTimeOffset.UtcNow, Status = status
        };
        dbContext.Set<CODCollection>().Add(collection);
        await dbContext.SaveChangesAsync();
        return collection;
    }

    private static async Task<MerchantSettlement> SeedMerchantSettlementAsync(TestDbContext dbContext, Guid organizationId, Guid merchantId)
    {
        var settlement = new MerchantSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, MerchantId = merchantId,
            PeriodFrom = DateTimeOffset.UtcNow.AddDays(-7), PeriodTo = DateTimeOffset.UtcNow,
            TotalPayable = 2350m, Status = MerchantSettlementStatus.Created
        };
        dbContext.Set<MerchantSettlement>().Add(settlement);
        await dbContext.SaveChangesAsync();
        return settlement;
    }

    [Fact]
    public async Task ListCollectionsAsync_StaffWithRestrictedBranchAccess_OnlySeesAccessibleBranch()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var accessibleBranchId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();
        var accessible = await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), accessibleBranchId, CodCollectionStatus.CodExpected, null);
        await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), otherBranchId, CodCollectionStatus.CodExpected, null);
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker(accessibleBranchId));

        var results = await service.ListCollectionsAsync(null, null, null, null, null, StaffActingUser(Guid.NewGuid()));

        var result = Assert.Single(results.Items);
        Assert.Equal(accessible.Id, result.Id);
    }

    [Fact]
    public async Task ListCollectionsAsync_MerchantCaller_OnlySeesOwnMerchantRegardlessOfBranchAccess()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var own = await SeedCollectionAsync(dbContext, organizationId, merchantId, Guid.NewGuid(), CodCollectionStatus.CodExpected, null);
        await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid(), CodCollectionStatus.CodExpected, null);
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var results = await service.ListCollectionsAsync(null, null, null, null, null, MerchantActingUser(merchantId));

        var result = Assert.Single(results.Items);
        Assert.Equal(own.Id, result.Id);
    }

    [Fact]
    public async Task GetReconciliationSummaryAsync_ComputesEachFigureFromTheRightStatusSet()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), branchId, CodCollectionStatus.CodExpected, null); // ExpectedAmount 2500, not yet collected
        await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), branchId, CodCollectionStatus.CodCollected, 1000m);
        await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), branchId, CodCollectionStatus.BranchReconciled, 500m);
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var summary = await service.GetReconciliationSummaryAsync(null, null, null, null, StaffActingUser(Guid.NewGuid()));

        Assert.Equal(2500m, summary.ExpectedCod); // only the CodExpected row
        Assert.Equal(1500m, summary.CollectedCod); // 1000 + 500, both have a CollectedAmount
        Assert.Equal(500m, summary.RiderDepositedCod); // only BranchReconciled has reached RiderDeposited+
        Assert.Equal(500m, summary.BranchReceivedCod); // only BranchReconciled has reached BranchReconciled+
        Assert.Equal(300m, summary.TotalCourierFees); // 150 + 150, both collected rows
    }

    // --- Cross-tenant regression coverage ---
    // CodService is constructed against the generic DbContext type (not concretely AppDbContext),
    // so the production global HasQueryFilter on OrganizationId is not guaranteed by the type
    // system, and this module's TestDbContext above deliberately has no such filter (only
    // ApplyConfiguration calls) — so these tests can only pass if CodService.Reads.cs applies its
    // own explicit OrganizationId filter in every one of the five read methods.

    [Fact]
    public async Task GetCollectionByIdAsync_ForeignOrganization_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        var foreignCollection = await SeedCollectionAsync(dbContext, foreignOrganizationId, Guid.NewGuid(), Guid.NewGuid(), CodCollectionStatus.CodExpected, null);
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.GetCollectionByIdAsync(foreignCollection.Id, StaffActingUser(Guid.NewGuid()));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCollectionByIdAsync_StaffWithoutBranchAccess_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var accessibleBranchId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();
        var outOfScope = await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), otherBranchId, CodCollectionStatus.CodExpected, null);
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker(accessibleBranchId));

        var result = await service.GetCollectionByIdAsync(outOfScope.Id, StaffActingUser(Guid.NewGuid()));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCollectionByIdAsync_StaffWithBranchAccess_ReturnsCollection()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var accessibleBranchId = Guid.NewGuid();
        var accessible = await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), accessibleBranchId, CodCollectionStatus.CodExpected, null);
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker(accessibleBranchId));

        var result = await service.GetCollectionByIdAsync(accessible.Id, StaffActingUser(Guid.NewGuid()));

        Assert.NotNull(result);
        Assert.Equal(accessible.Id, result!.Id);
    }

    [Fact]
    public async Task GetCollectionByIdAsync_MerchantCallerForAnotherMerchantsCollection_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var otherMerchantsCollection = await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid(), CodCollectionStatus.CodExpected, null);
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.GetCollectionByIdAsync(otherMerchantsCollection.Id, MerchantActingUser(Guid.NewGuid()));

        Assert.Null(result);
    }

    [Fact]
    public async Task ListCollectionsAsync_ExcludesForeignOrganizationCollections()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        var own = await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid(), CodCollectionStatus.CodExpected, null);
        await SeedCollectionAsync(dbContext, foreignOrganizationId, Guid.NewGuid(), Guid.NewGuid(), CodCollectionStatus.CodExpected, null);
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var results = await service.ListCollectionsAsync(null, null, null, null, null, StaffActingUser(Guid.NewGuid()));

        var result = Assert.Single(results.Items);
        Assert.Equal(own.Id, result.Id);
    }

    [Fact]
    public async Task GetReconciliationSummaryAsync_ExcludesForeignOrganizationCollections()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid(), CodCollectionStatus.CodExpected, null);
        // Same amounts as the in-org row, seeded under a different organization — if the org filter
        // were missing, this would double every figure in the summary below.
        await SeedCollectionAsync(dbContext, foreignOrganizationId, Guid.NewGuid(), Guid.NewGuid(), CodCollectionStatus.CodExpected, null);
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var summary = await service.GetReconciliationSummaryAsync(null, null, null, null, StaffActingUser(Guid.NewGuid()));

        Assert.Equal(2500m, summary.ExpectedCod);
    }

    [Fact]
    public async Task ListMerchantSettlementsAsync_ExcludesForeignOrganizationSettlements()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        var own = await SeedMerchantSettlementAsync(dbContext, organizationId, Guid.NewGuid());
        await SeedMerchantSettlementAsync(dbContext, foreignOrganizationId, Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var results = await service.ListMerchantSettlementsAsync(null, StaffActingUser(Guid.NewGuid()));

        var result = Assert.Single(results);
        Assert.Equal(own.Id, result.Id);
    }

    [Fact]
    public async Task GetMerchantSettlementByIdAsync_ForeignOrganization_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        var foreignSettlement = await SeedMerchantSettlementAsync(dbContext, foreignOrganizationId, Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.GetMerchantSettlementByIdAsync(foreignSettlement.Id, StaffActingUser(Guid.NewGuid()));

        Assert.Null(result);
    }
}
