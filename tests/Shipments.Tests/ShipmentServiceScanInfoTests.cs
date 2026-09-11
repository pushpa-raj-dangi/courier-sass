using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Events.Application;
using Courier.Geography.Application;
using Courier.Identity.Application;
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
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    private readonly Dictionary<Guid, Guid?> _zonesByBranch;
    public FakeBranchLookup(Dictionary<Guid, Guid?> zonesByBranch) => _zonesByBranch = zonesByBranch;
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_zonesByBranch.TryGetValue(branchId, out var zoneId) ? new BranchZoneInfo(branchId, zoneId) : null);
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

file class AlwaysTrueGeographyLookup : IGeographyLookupService
{
    public Task<bool> IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid localLevelId, int wardNumber, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> DeliveryZoneExistsAsync(Guid zoneId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class FixedOrganizationLookup : IOrganizationLookupService
{
    public Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<PagedResult<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SuspendForNonPaymentAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    private readonly string? _slug;
    public FixedOrganizationLookup(string? slug) => _slug = slug;
    public Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(_slug);
    public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<PagedResult<OrganizationRecord>> ListAsync(string? search, bool? isActive, bool? isDeleted, int? page, int? pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<OrganizationRecord>(new List<OrganizationRecord>(), 1, 25, 0));
    public Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

// Phase 10 — this file doesn't exercise COD collection creation, just satisfies ShipmentService's
// new constructor dependency (Task 5).
file class NoopCodCollectionCreationService : ICodCollectionCreationService
{
    public Task CreateExpectedAsync(Guid shipmentId, Guid organizationId, Guid merchantId, Guid branchId, decimal expectedAmount, decimal shipmentTotalCharge, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class NoopOutboxWriter : IOutboxWriter
{
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class ShipmentServiceScanInfoTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
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

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    private static ShipmentService CreateService(TestDbContext dbContext, Guid organizationId, Dictionary<Guid, Guid?> zonesByBranch)
    {
        var branchLookup = new FakeBranchLookup(zonesByBranch);
        var pricingService = new PricingService(dbContext, new FixedTenantProvider(organizationId), branchLookup, new AlwaysTrueMerchantLookup());
        return new ShipmentService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), branchLookup,
            new AlwaysTrueGeographyLookup(), new AlwaysTrueMerchantLookup(), pricingService, new AllBranchesAuthorizationChecker(),
            new FixedOrganizationLookup("testorg"), new NoopCodCollectionCreationService(), new NoopOutboxWriter());
    }

    private static async Task<(ShipmentService Service, Guid ShipmentId, string Awb, Guid OriginBranchId, Guid DestinationBranchId)> SeedCreatedShipmentAsync(TestDbContext dbContext, Guid organizationId)
    {
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;

        // Reuse the org/rate-card row across multiple calls for the same organizationId (a test
        // may seed two shipments for one org) instead of re-adding them, which EF would reject as
        // a duplicate-key tracking conflict on Organization and which would also violate "only one
        // active RateCard per org" (Phase 5) if a second RateCard were inserted.
        if (!dbContext.Set<Organization>().Any(o => o.Id == organizationId))
        {
            dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Test Org", Slug = "testorg" });
        }
        var rateCard = dbContext.Set<RateCard>().FirstOrDefault(r => r.OrganizationId == organizationId);
        if (rateCard is null)
        {
            rateCard = new RateCard { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Default", EffectiveFrom = DateTimeOffset.UtcNow, Version = 1, IsActive = true };
            dbContext.Set<RateCard>().Add(rateCard);
        }
        dbContext.Set<ZoneRate>().Add(new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = weightSlabId, BaseRate = 100m });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, organizationId, new() { [originBranchId] = originZoneId, [destinationBranchId] = destinationZoneId });
        var request = new CreateShipmentRequest(
            Guid.NewGuid(), originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
            "Recipient", "9800000000", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Recipient Address",
            "Sender", "9800000001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Sender Address",
            new List<CreateShipmentItemRequest> { new("Item", 1, 100m) });
        var created = await service.CreateShipmentAsync(request, ActingUser(Guid.NewGuid()));

        return (service, created.Id, created.Awb, originBranchId, destinationBranchId);
    }

    [Fact]
    public async Task GetScanInfoAsync_ExistingShipment_ReturnsProjection()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, awb, originBranchId, destinationBranchId) = await SeedCreatedShipmentAsync(dbContext, organizationId);

        IShipmentTransitionService transitionService = service;
        var info = await transitionService.GetScanInfoAsync(shipmentId);

        Assert.NotNull(info);
        Assert.Equal(shipmentId, info!.ShipmentId);
        Assert.Equal(organizationId, info.OrganizationId);
        Assert.Equal(awb, info.Awb);
        Assert.Equal(ShipmentStatus.Created, info.CurrentStatus);
        Assert.Equal(originBranchId, info.CurrentBranchId);
        Assert.Equal(originBranchId, info.OriginBranchId);
        Assert.Equal(destinationBranchId, info.DestinationBranchId);
    }

    [Fact]
    public async Task GetScanInfoAsync_UnknownShipment_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid(), new());
        IShipmentTransitionService transitionService = service;

        Assert.Null(await transitionService.GetScanInfoAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetScanInfoByAwbAsync_ExistingShipment_ReturnsProjection()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, awb, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        IShipmentTransitionService transitionService = service;

        var info = await transitionService.GetScanInfoByAwbAsync(awb);

        Assert.NotNull(info);
        Assert.Equal(shipmentId, info!.ShipmentId);
    }

    [Fact]
    public async Task GetScanInfoByAwbAsync_UnknownAwb_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid(), new());
        IShipmentTransitionService transitionService = service;

        Assert.Null(await transitionService.GetScanInfoByAwbAsync("NOSUCHAWB"));
    }

    [Fact]
    public async Task DispatchManyAsync_MultipleSortedShipments_AllMoveToInTransit()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId1, _, originBranchId, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());
        await service.AssignPickupToRiderAsync(shipmentId1, Guid.NewGuid(), actingUser);
        await service.RiderPickupScanAsync(shipmentId1, Guid.NewGuid(), actingUser);
        await service.ReceiveAtOriginAsync(shipmentId1, actingUser);
        await service.SortAsync(shipmentId1, actingUser);

        var seed2 = await SeedCreatedShipmentAsync(dbContext, organizationId);
        var shipmentId2 = seed2.ShipmentId;
        await service.AssignPickupToRiderAsync(shipmentId2, Guid.NewGuid(), actingUser);
        await service.RiderPickupScanAsync(shipmentId2, Guid.NewGuid(), actingUser);
        await service.ReceiveAtOriginAsync(shipmentId2, actingUser);
        await service.SortAsync(shipmentId2, actingUser);

        IShipmentTransitionService transitionService = service;
        await transitionService.DispatchManyAsync(new[] { shipmentId1, shipmentId2 }, actingUser);

        var info1 = await transitionService.GetScanInfoAsync(shipmentId1);
        var info2 = await transitionService.GetScanInfoAsync(shipmentId2);
        Assert.Equal(ShipmentStatus.InTransit, info1!.CurrentStatus);
        Assert.Null(info1.CurrentBranchId);
        Assert.Equal(ShipmentStatus.InTransit, info2!.CurrentStatus);

        var dispatchedEvents = await dbContext.Set<ShipmentEvent>()
            .Where(e => e.EventType == ShipmentEventType.Dispatched)
            .ToListAsync();
        Assert.Equal(2, dispatchedEvents.Count);
    }

    [Fact]
    public async Task DispatchManyAsync_ShipmentNotSorted_ThrowsInvalidTransitionAndAppliesNothing()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, _, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        IShipmentTransitionService transitionService = service;

        // Still Created, never sorted.
        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => transitionService.DispatchManyAsync(new[] { shipmentId }, ActingUser(Guid.NewGuid())));

        var info = await transitionService.GetScanInfoAsync(shipmentId);
        Assert.Equal(ShipmentStatus.Created, info!.CurrentStatus);
    }
}
