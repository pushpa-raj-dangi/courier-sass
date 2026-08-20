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
using Courier.Shipments.Domain;
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

public class ShipmentServiceCreationTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Organization must be part of the model: ShipmentService.GenerateAwbAsync queries
            // _dbContext.Set<Courier.Identity.Domain.Organization>() directly to derive the AWB
            // prefix from the org's Slug (falling back to the OrganizationId when no row exists
            // or the slug has no alphanumeric characters). EF Core's InMemory provider throws at
            // query time for any Set<T>() whose entity type isn't reachable from OnModelCreating,
            // so this configuration is required even though these tests never seed a matching row
            // for most organizationIds (that's the fallback path being exercised).
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
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", userId.ToString())
    }, "test"));

    private static (Guid OriginBranchId, Guid DestinationBranchId, Guid OriginZoneId, Guid DestinationZoneId, Guid ServiceTypeId) SeedRoutableRateCard(TestDbContext dbContext, Guid organizationId, decimal baseRate = 150m)
    {
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;

        // Seeds a real Organization row (with a Slug) so GenerateAwbAsync's primary path — deriving
        // the AWB prefix from the org's slug rather than falling back to the OrganizationId — is
        // exercised by these tests, not just its fallback branch.
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Test Org", Slug = "TESTORG" });

        var rateCard = new RateCard { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Default", EffectiveFrom = DateTimeOffset.UtcNow, Version = 1, IsActive = true };
        dbContext.Set<RateCard>().Add(rateCard);
        dbContext.Set<ZoneRate>().Add(new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = weightSlabId, BaseRate = baseRate });
        dbContext.SaveChanges();

        return (originBranchId, destinationBranchId, originZoneId, destinationZoneId, serviceTypeId);
    }

    private static ShipmentService CreateService(TestDbContext dbContext, Guid organizationId, Dictionary<Guid, Guid?> zonesByBranch)
    {
        var branchLookup = new FakeBranchLookup(zonesByBranch);
        var pricingService = new PricingService(dbContext, new FixedTenantProvider(organizationId), branchLookup, new AlwaysTrueMerchantLookup());
        return new ShipmentService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), branchLookup,
            new AlwaysTrueGeographyLookup(), new AlwaysTrueMerchantLookup(), pricingService, new AllBranchesAuthorizationChecker());
    }

    private static CreateShipmentRequest ValidRequest(Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, decimal weightKg = 0.7m, decimal codAmount = 0m, bool wantsInsurance = false, Guid? merchantId = null) => new(
        merchantId ?? Guid.NewGuid(), originBranchId, destinationBranchId, serviceTypeId, weightKg, null, null, null, codAmount, wantsInsurance,
        "Recipient One", "9800000000", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Recipient Address",
        "Sender One", "9800000001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 2, 500m) });

    [Fact]
    public async Task CreateShipmentAsync_ValidRequest_ComputesDeclaredValueFromItems()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedRoutableRateCard(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new() { [seed.OriginBranchId] = seed.OriginZoneId, [seed.DestinationBranchId] = seed.DestinationZoneId });

        var result = await service.CreateShipmentAsync(ValidRequest(seed.OriginBranchId, seed.DestinationBranchId, seed.ServiceTypeId), ActingUser(Guid.NewGuid()));

        Assert.Equal(1000m, result.DeclaredValue); // 2 * 500
        Assert.Equal(150m, result.TotalCharge);
        Assert.Equal(ShipmentStatus.Created, result.CurrentStatus);
    }

    [Fact]
    public async Task CreateShipmentAsync_ValidRequest_GeneratesUniqueAwbWithOrgPrefix()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedRoutableRateCard(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new() { [seed.OriginBranchId] = seed.OriginZoneId, [seed.DestinationBranchId] = seed.DestinationZoneId });

        var first = await service.CreateShipmentAsync(ValidRequest(seed.OriginBranchId, seed.DestinationBranchId, seed.ServiceTypeId), ActingUser(Guid.NewGuid()));
        var second = await service.CreateShipmentAsync(ValidRequest(seed.OriginBranchId, seed.DestinationBranchId, seed.ServiceTypeId), ActingUser(Guid.NewGuid()));

        Assert.NotEqual(first.Awb, second.Awb);
        Assert.Matches(@"^[A-Z0-9]{1,6}\d{8}\d{6}$", first.Awb);
    }

    [Fact]
    public async Task CreateShipmentAsync_ValidRequest_WritesSingleCreatedEvent()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedRoutableRateCard(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new() { [seed.OriginBranchId] = seed.OriginZoneId, [seed.DestinationBranchId] = seed.DestinationZoneId });

        var result = await service.CreateShipmentAsync(ValidRequest(seed.OriginBranchId, seed.DestinationBranchId, seed.ServiceTypeId), ActingUser(Guid.NewGuid()));

        var events = await dbContext.Set<ShipmentEvent>().Where(e => e.ShipmentId == result.Id).ToListAsync();
        var created = Assert.Single(events);
        Assert.Equal(ShipmentEventType.Created, created.EventType);
        Assert.Null(created.PreviousStatus);
        Assert.Equal(ShipmentStatus.Created, created.NewStatus);
        Assert.Equal(seed.OriginBranchId, created.BranchId);
    }

    [Fact]
    public async Task CreateShipmentAsync_EmptyItems_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedRoutableRateCard(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new() { [seed.OriginBranchId] = seed.OriginZoneId, [seed.DestinationBranchId] = seed.DestinationZoneId });
        var request = ValidRequest(seed.OriginBranchId, seed.DestinationBranchId, seed.ServiceTypeId) with { Items = new List<CreateShipmentItemRequest>() };

        await Assert.ThrowsAsync<ShipmentValidationException>(() => service.CreateShipmentAsync(request, ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateShipmentAsync_NoRateDefined_ThrowsShipmentValidationException()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        // No RateCard/ZoneRate seeded at all for this org.
        var service = CreateService(dbContext, organizationId, new() { [originBranchId] = originZoneId, [destinationBranchId] = destinationZoneId });

        await Assert.ThrowsAsync<ShipmentValidationException>(() =>
            service.CreateShipmentAsync(ValidRequest(originBranchId, destinationBranchId, serviceTypeId), ActingUser(Guid.NewGuid())));
    }
}
