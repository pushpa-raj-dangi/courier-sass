using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
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

// Denies access to every branch — used to prove PickupScanAsync/DeliverAsync actually enforce
// CheckBranchAccessAsync rather than skipping it (unlike AllBranchesAuthorizationChecker's
// "null = unrestricted" convention, this returns an empty accessible set, so every branch check
// fails).
file class NoBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(new HashSet<Guid>());
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

// Replaces ShipmentService's former direct _dbContext.Set<Organization>() reach into Identity's
// EF-mapped table (Fix 6) — returns a fixed slug so GenerateAwbAsync's primary (non-fallback)
// path is still exercised without the test needing a real IOrganizationLookupService/DbContext
// wiring.
file class FixedOrganizationLookup : IOrganizationLookupService
{
    private readonly string? _slug;
    public FixedOrganizationLookup(string? slug) => _slug = slug;
    public Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(_slug);
}

// Phase 10 — this file doesn't exercise COD collection creation, just satisfies ShipmentService's
// new constructor dependency (Task 5).
file class NoopCodCollectionCreationService : ICodCollectionCreationService
{
    public Task CreateExpectedAsync(Guid shipmentId, Guid organizationId, Guid merchantId, Guid branchId, decimal expectedAmount, decimal shipmentTotalCharge, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class ShipmentServiceTransitionTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Organization is kept in the model/seeded below for fidelity, even though
            // ShipmentService.GenerateAwbAsync no longer queries it directly (Fix 6: AWB-prefix
            // derivation now goes through IOrganizationLookupService/FixedOrganizationLookup above).
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

    private static ShipmentService CreateService(TestDbContext dbContext, Guid organizationId, Dictionary<Guid, Guid?> zonesByBranch, IBranchAuthorizationChecker? branchAuthorizationChecker = null)
    {
        var branchLookup = new FakeBranchLookup(zonesByBranch);
        var pricingService = new PricingService(dbContext, new FixedTenantProvider(organizationId), branchLookup, new AlwaysTrueMerchantLookup());
        return new ShipmentService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), branchLookup,
            new AlwaysTrueGeographyLookup(), new AlwaysTrueMerchantLookup(), pricingService, branchAuthorizationChecker ?? new AllBranchesAuthorizationChecker(),
            new FixedOrganizationLookup("testorg"), new NoopCodCollectionCreationService());
    }

    private static async Task<(ShipmentService Service, Guid ShipmentId, Guid OriginBranchId, Guid DestinationBranchId)> SeedCreatedShipmentAsync(TestDbContext dbContext, Guid organizationId)
    {
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;

        // Seeds a real Organization row for model fidelity; matches CreateService's
        // FixedOrganizationLookup("testorg") default used by GenerateAwbAsync's primary path.
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
        var riderId = Guid.NewGuid();

        var afterAssign = await service.AssignPickupToRiderAsync(shipmentId, riderId, actingUser);
        Assert.Equal(ShipmentStatus.PickupAssigned, afterAssign!.CurrentStatus);
        Assert.Equal(riderId, afterAssign.CurrentCustodianRiderId);

        var afterScan = await service.RiderPickupScanAsync(shipmentId, riderId, actingUser);
        Assert.Equal(ShipmentStatus.PickedUp, afterScan!.CurrentStatus);
        Assert.Equal(riderId, afterScan.CurrentCustodianRiderId);

        var afterReceiveOrigin = await service.ReceiveAtOriginAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.AtOriginBranch, afterReceiveOrigin!.CurrentStatus);
        Assert.Equal(originBranchId, afterReceiveOrigin.CurrentBranchId);
        Assert.Null(afterReceiveOrigin.CurrentCustodianRiderId);

        var afterSort = await service.SortAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.Sorted, afterSort!.CurrentStatus);
        Assert.Equal(originBranchId, afterSort.CurrentBranchId);

        await service.DispatchManyAsync(new[] { shipmentId }, actingUser);
        var afterDispatch = await dbContext.Set<Shipment>().FirstAsync(s => s.Id == shipmentId);
        Assert.Equal(ShipmentStatus.InTransit, afterDispatch.CurrentStatus);
        Assert.Null(afterDispatch.CurrentBranchId);

        var afterReceiveDest = await service.ReceiveAtDestinationAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.AtDestinationBranch, afterReceiveDest!.CurrentStatus);
        Assert.Equal(destinationBranchId, afterReceiveDest.CurrentBranchId);

        var afterOutForDelivery = await service.AssignDeliveryToRiderAsync(shipmentId, riderId, actingUser);
        Assert.Equal(ShipmentStatus.OutForDelivery, afterOutForDelivery!.CurrentStatus);
        Assert.Equal(riderId, afterOutForDelivery.CurrentCustodianRiderId);
        Assert.Null(afterOutForDelivery.CurrentBranchId);

        var afterDeliver = await service.CompleteDeliveryAsync(shipmentId, riderId, "Recipient", actingUser);
        Assert.Equal(ShipmentStatus.Delivered, afterDeliver!.CurrentStatus);
        Assert.Null(afterDeliver.CurrentCustodianRiderId);
        Assert.Null(afterDeliver.CurrentBranchId);

        var events = await dbContext.Set<ShipmentEvent>().Where(e => e.ShipmentId == shipmentId).OrderBy(e => e.Timestamp).ToListAsync();
        Assert.Equal(9, events.Count);
        Assert.Equal(new[]
        {
            ShipmentEventType.Created, ShipmentEventType.PickupAssigned, ShipmentEventType.PickedUp,
            ShipmentEventType.ReceivedAtOrigin, ShipmentEventType.Sorted, ShipmentEventType.Dispatched,
            ShipmentEventType.ReceivedAtDestination, ShipmentEventType.OutForDelivery, ShipmentEventType.Delivered
        }, events.Select(e => e.EventType));
        Assert.All(events.Where(e => e.EventType is ShipmentEventType.PickupAssigned or ShipmentEventType.PickedUp or ShipmentEventType.OutForDelivery or ShipmentEventType.Delivered),
            e => Assert.Equal(riderId, e.RiderId));

        var dispatchedEvent = events.Single(e => e.EventType == ShipmentEventType.Dispatched);
        Assert.Equal(originBranchId, dispatchedEvent.BranchId);
        var outForDeliveryEvent = events.Single(e => e.EventType == ShipmentEventType.OutForDelivery);
        Assert.Equal(destinationBranchId, outForDeliveryEvent.BranchId);
    }

    [Fact]
    public async Task FailDeliveryAsync_FromOutForDelivery_TransitionsToDeliveryFailedAndKeepsRiderCustody()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());
        var riderId = Guid.NewGuid();
        await service.AssignPickupToRiderAsync(shipmentId, riderId, actingUser);
        await service.RiderPickupScanAsync(shipmentId, riderId, actingUser);
        await service.ReceiveAtOriginAsync(shipmentId, actingUser);
        await service.SortAsync(shipmentId, actingUser);
        await service.DispatchManyAsync(new[] { shipmentId }, actingUser);
        await service.ReceiveAtDestinationAsync(shipmentId, actingUser);
        await service.AssignDeliveryToRiderAsync(shipmentId, riderId, actingUser);

        var result = await service.FailDeliveryAsync(shipmentId, riderId, actingUser);

        Assert.Equal(ShipmentStatus.DeliveryFailed, result!.CurrentStatus);
        // Regression: custody must NOT be cleared on failure — the parcel is still physically with
        // the rider, it hasn't gone back to a branch (this file's Task 2 design note).
        Assert.Equal(riderId, result.CurrentCustodianRiderId);
    }

    [Fact]
    public async Task PickupScanAsync_CallerDeniedBranchAccess_IsEnforcedByRidersModuleNotShipments()
    {
        // Regression guard for the Phase 7/8 division of responsibility: RiderPickupScanAsync no
        // longer performs its own branch-access check (Riders.Application.RiderService does, before
        // ever calling in) — proven here by showing a "denied" IBranchAuthorizationChecker has NO
        // effect on this method, unlike the old PickupScanAsync it replaced.
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());
        var riderId = Guid.NewGuid();
        await service.AssignPickupToRiderAsync(shipmentId, riderId, actingUser);

        var deniedService = CreateService(dbContext, organizationId, new(), new NoBranchesAuthorizationChecker());
        var result = await deniedService.RiderPickupScanAsync(shipmentId, riderId, actingUser);

        Assert.Equal(ShipmentStatus.PickedUp, result!.CurrentStatus);
    }

    [Fact]
    public async Task PickupScanAsync_FromCreatedStatus_ThrowsInvalidTransition()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);

        var ex = await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => service.RiderPickupScanAsync(shipmentId, Guid.NewGuid(), ActingUser(Guid.NewGuid())));
        Assert.Equal(ShipmentStatus.Created, ex.From);
        Assert.Equal(ShipmentStatus.PickedUp, ex.To);
    }

    [Fact]
    public async Task CancelAsync_FromCreated_Succeeds()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, originBranchId, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);

        var result = await service.CancelAsync(shipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.Cancelled, result!.CurrentStatus);

        var cancelledEvent = await dbContext.Set<ShipmentEvent>()
            .Where(e => e.ShipmentId == shipmentId && e.EventType == ShipmentEventType.Cancelled)
            .SingleAsync();
        Assert.Equal(originBranchId, cancelledEvent.BranchId);
    }

    [Fact]
    public async Task CancelAsync_FromPickedUp_ThrowsInvalidTransition()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());
        await service.AssignPickupToRiderAsync(shipmentId, Guid.NewGuid(), actingUser);
        await service.RiderPickupScanAsync(shipmentId, Guid.NewGuid(), actingUser);

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => service.CancelAsync(shipmentId, actingUser));
    }
}
