using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Geography.Application;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Pricing.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Shipments.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FakeBranchLookup : IBranchLookupService
{
    private readonly Dictionary<Guid, Guid?> _zonesByBranch;
    public FakeBranchLookup(Dictionary<Guid, Guid?> zonesByBranch) => _zonesByBranch = zonesByBranch;
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_zonesByBranch.TryGetValue(branchId, out var zoneId) ? new BranchZoneInfo(branchId, zoneId) : null);
}

file class AlwaysTrueGeographyLookup : IGeographyLookupService
{
    public Task<bool> IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid localLevelId, int wardNumber, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> DeliveryZoneExistsAsync(Guid zoneId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

public class ShipmentServiceReadTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Organization must be part of the model: ShipmentService.GenerateAwbAsync (invoked
            // transitively via CreateShipmentAsync in SeedShipmentForMerchantAsync below) queries
            // _dbContext.Set<Courier.Identity.Domain.Organization>() directly. EF Core's InMemory
            // provider throws at query time for any Set<T>() whose entity type isn't reachable
            // from OnModelCreating — same gap Tasks 6/7 hit, fixed the same way here.
            modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
            modelBuilder.ApplyConfiguration(new ServiceTypeConfiguration());
            modelBuilder.ApplyConfiguration(new WeightSlabConfiguration());
            modelBuilder.ApplyConfiguration(new RateCardConfiguration());
            modelBuilder.ApplyConfiguration(new ZoneRateConfiguration());
            modelBuilder.ApplyConfiguration(new AdditionalChargeConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantRateCardConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentItemConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentEventConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal StaffUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));
    private static ClaimsPrincipal MerchantUser(Guid merchantId) => new(new ClaimsIdentity(new[] { new Claim("merchant_id", merchantId.ToString()) }, "test"));

    private static ShipmentService CreateService(TestDbContext dbContext, Guid organizationId, Dictionary<Guid, Guid?> zonesByBranch)
    {
        var branchLookup = new FakeBranchLookup(zonesByBranch);
        var pricingService = new PricingService(dbContext, new FixedTenantProvider(organizationId), branchLookup, new AlwaysTrueMerchantLookup());
        return new ShipmentService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), branchLookup,
            new AlwaysTrueGeographyLookup(), new AlwaysTrueMerchantLookup(), pricingService, new AllBranchesAuthorizationChecker());
    }

    private static async Task<ShipmentResponse> SeedShipmentForMerchantAsync(TestDbContext dbContext, Guid organizationId, Guid merchantId)
    {
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;

        // Seed a real Organization row (with a Slug) so GenerateAwbAsync (called transitively via
        // CreateShipmentAsync below) has a row to query instead of throwing/falling back — same
        // fix Tasks 6/7 applied. Only seed once per organizationId across the multiple
        // SeedShipmentForMerchantAsync calls some tests make.
        if (!dbContext.Set<Organization>().Any(o => o.Id == organizationId))
        {
            dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Test Org", Slug = "testorg" });
        }

        // Reuse a single active RateCard per organizationId rather than creating a new one per
        // call: some tests below call this helper twice for the same org (two merchants sharing
        // an organization). PricingService.QuoteAsync resolves the RateCard via an unordered
        // FirstOrDefaultAsync(rc => rc.IsActive) with no merchant-specific override configured —
        // if a second call created a second IsActive RateCard, that lookup could non-deterministically
        // pick the wrong RateCard, whose ZoneRate rows don't match this call's randomly generated
        // zone ids, causing a spurious "No rate is defined..." failure unrelated to the read
        // methods under test. Keeping one RateCard per org (with one ZoneRate row added per call,
        // scoped to that call's own zone ids) keeps the resolution deterministic.
        var rateCard = dbContext.Set<RateCard>().FirstOrDefault(rc => rc.OrganizationId == organizationId);
        if (rateCard is null)
        {
            rateCard = new RateCard { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Default", EffectiveFrom = DateTimeOffset.UtcNow, Version = 1, IsActive = true };
            dbContext.Set<RateCard>().Add(rateCard);
        }
        dbContext.Set<ZoneRate>().Add(new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = weightSlabId, BaseRate = 100m });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, organizationId, new() { [originBranchId] = originZoneId, [destinationBranchId] = destinationZoneId });
        var request = new CreateShipmentRequest(
            merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
            "Recipient", "9800000000", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Recipient Address",
            "Sender", "9800000001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Sender Address",
            new List<CreateShipmentItemRequest> { new("Item", 1, 100m) });
        return await service.CreateShipmentAsync(request, StaffUser(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetByAwbAsync_MerchantOwnsShipment_ReturnsIt()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var created = await SeedShipmentForMerchantAsync(dbContext, organizationId, merchantId);
        var service = CreateService(dbContext, organizationId, new());

        var result = await service.GetByAwbAsync(created.Awb, MerchantUser(merchantId));

        Assert.NotNull(result);
        Assert.Equal(created.Id, result!.Id);
    }

    [Fact]
    public async Task GetByAwbAsync_DifferentMerchant_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        var created = await SeedShipmentForMerchantAsync(dbContext, organizationId, merchantId);
        var service = CreateService(dbContext, organizationId, new());

        var result = await service.GetByAwbAsync(created.Awb, MerchantUser(otherMerchantId));

        Assert.Null(result);
    }

    [Fact]
    public async Task ListAsync_MerchantCaller_OnlySeesOwnShipments()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        await SeedShipmentForMerchantAsync(dbContext, organizationId, merchantId);
        await SeedShipmentForMerchantAsync(dbContext, organizationId, otherMerchantId);
        var service = CreateService(dbContext, organizationId, new());

        var result = await service.ListAsync(MerchantUser(merchantId));

        Assert.All(result, s => Assert.Equal(merchantId, s.MerchantId));
    }

    [Fact]
    public async Task ListAsync_StaffCaller_SeesAllOrgShipments()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new());

        var result = await service.ListAsync(StaffUser(Guid.NewGuid()));

        Assert.Equal(2, result.Count);
    }
}
