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

// Fakes duplicated per-file (file-scoped) matching ShipmentServiceCreationTests.cs's pattern —
// mirrors how RateCardServiceTests.cs/PricingServiceTests.cs in Phase 5 each define their own
// file-scoped fakes rather than sharing a fixture file, since each test file is independently
// readable without hunting for a shared fixtures file.
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

public class ShipmentServiceTransitionTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Organization must be part of the model: ShipmentService.GenerateAwbAsync (invoked
            // transitively via CreateShipmentAsync in SeedCreatedShipmentAsync below) queries
            // _dbContext.Set<Courier.Identity.Domain.Organization>() directly. EF Core's InMemory
            // provider throws at query time for any Set<T>() whose entity type isn't reachable
            // from OnModelCreating — same gap Task 6 hit, fixed the same way here.
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
            new AlwaysTrueGeographyLookup(), new AlwaysTrueMerchantLookup(), pricingService, new AllBranchesAuthorizationChecker());
    }

    private static async Task<(ShipmentService Service, Guid ShipmentId, Guid OriginBranchId, Guid DestinationBranchId)> SeedCreatedShipmentAsync(TestDbContext dbContext, Guid organizationId)
    {
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;

        // Seed a real Organization row (with a Slug) so GenerateAwbAsync (called transitively via
        // CreateShipmentAsync below) has a row to query instead of throwing/falling back — same
        // fix Task 6 applied in ShipmentServiceCreationTests.cs's SeedRoutableRateCard.
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Test Org", Slug = "testorg" });

        var rateCard = new RateCard { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Default", EffectiveFrom = DateTimeOffset.UtcNow, Version = 1, IsActive = true };
        dbContext.Set<RateCard>().Add(rateCard);
        dbContext.Set<ZoneRate>().Add(new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = weightSlabId, BaseRate = 100m });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, organizationId, new() { [originBranchId] = originZoneId, [destinationBranchId] = destinationZoneId });
        var request = new CreateShipmentRequest(
            Guid.NewGuid(), originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
            "Recipient", "9800000000", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Recipient Address",
            "Sender", "9800000001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Sender Address",
            new List<CreateShipmentItemRequest> { new("Item", 1, 100m) });
        var created = await service.CreateShipmentAsync(request, ActingUser(Guid.NewGuid()));

        return (service, created.Id, originBranchId, destinationBranchId);
    }

    [Fact]
    public async Task FullHappyPath_CreatedToDelivered_WalksEveryTransitionWithCorrectEventsAndCustody()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, originBranchId, destinationBranchId) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());
        var riderUserId = Guid.NewGuid();

        var afterAssign = await service.AssignPickupAsync(shipmentId, new AssignPickupRequest(riderUserId), actingUser);
        Assert.Equal(ShipmentStatus.PickupAssigned, afterAssign!.CurrentStatus);
        Assert.Equal(riderUserId, afterAssign.CurrentCustodianUserId);

        var afterScan = await service.PickupScanAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.PickedUp, afterScan!.CurrentStatus);
        Assert.Equal(riderUserId, afterScan.CurrentCustodianUserId);

        var afterReceiveOrigin = await service.ReceiveAtOriginAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.AtOriginBranch, afterReceiveOrigin!.CurrentStatus);
        Assert.Equal(originBranchId, afterReceiveOrigin.CurrentBranchId);
        Assert.Null(afterReceiveOrigin.CurrentCustodianUserId);

        var afterSort = await service.SortAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.Sorted, afterSort!.CurrentStatus);
        Assert.Equal(originBranchId, afterSort.CurrentBranchId);

        var afterDispatch = await service.DispatchAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.InTransit, afterDispatch!.CurrentStatus);
        Assert.Null(afterDispatch.CurrentBranchId);

        var afterReceiveDest = await service.ReceiveAtDestinationAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.AtDestinationBranch, afterReceiveDest!.CurrentStatus);
        Assert.Equal(destinationBranchId, afterReceiveDest.CurrentBranchId);

        var afterOutForDelivery = await service.AssignDeliveryAsync(shipmentId, new AssignDeliveryRequest(riderUserId), actingUser);
        Assert.Equal(ShipmentStatus.OutForDelivery, afterOutForDelivery!.CurrentStatus);
        Assert.Equal(riderUserId, afterOutForDelivery.CurrentCustodianUserId);
        Assert.Null(afterOutForDelivery.CurrentBranchId);

        var afterDeliver = await service.DeliverAsync(shipmentId, new DeliverShipmentRequest("Recipient", "left at door"), actingUser);
        Assert.Equal(ShipmentStatus.Delivered, afterDeliver!.CurrentStatus);
        Assert.Null(afterDeliver.CurrentCustodianUserId);
        Assert.Null(afterDeliver.CurrentBranchId);

        var events = await dbContext.Set<ShipmentEvent>().Where(e => e.ShipmentId == shipmentId).OrderBy(e => e.Timestamp).ToListAsync();
        Assert.Equal(9, events.Count);
        Assert.Equal(new[]
        {
            ShipmentEventType.Created, ShipmentEventType.PickupAssigned, ShipmentEventType.PickedUp,
            ShipmentEventType.ReceivedAtOrigin, ShipmentEventType.Sorted, ShipmentEventType.Dispatched,
            ShipmentEventType.ReceivedAtDestination, ShipmentEventType.OutForDelivery, ShipmentEventType.Delivered
        }, events.Select(e => e.EventType));

        // Regression: TransitionAsync must capture BranchId BEFORE applyCustody runs. Dispatch and
        // AssignDelivery both null out Shipment.CurrentBranchId as part of their custody delta (the
        // parcel is now in transit / with a rider, not "at" a branch) — but the event itself must
        // still record which branch the operation was physically performed at, or the traceability
        // chain (CLAUDE.md §11/§12/§39) silently loses that branch for these two events.
        var dispatchedEvent = events.Single(e => e.EventType == ShipmentEventType.Dispatched);
        Assert.Equal(originBranchId, dispatchedEvent.BranchId);
        var outForDeliveryEvent = events.Single(e => e.EventType == ShipmentEventType.OutForDelivery);
        Assert.Equal(destinationBranchId, outForDeliveryEvent.BranchId);
    }

    [Fact]
    public async Task PickupScanAsync_FromCreatedStatus_ThrowsInvalidTransition()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);

        var ex = await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => service.PickupScanAsync(shipmentId, ActingUser(Guid.NewGuid())));
        Assert.Equal(ShipmentStatus.Created, ex.From);
        Assert.Equal(ShipmentStatus.PickedUp, ex.To);
    }

    [Fact]
    public async Task CancelAsync_FromCreated_Succeeds()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);

        var result = await service.CancelAsync(shipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.Cancelled, result!.CurrentStatus);
    }

    [Fact]
    public async Task CancelAsync_FromPickedUp_ThrowsInvalidTransition()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());
        await service.AssignPickupAsync(shipmentId, new AssignPickupRequest(Guid.NewGuid()), actingUser);
        await service.PickupScanAsync(shipmentId, actingUser);

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => service.CancelAsync(shipmentId, actingUser));
    }
}
