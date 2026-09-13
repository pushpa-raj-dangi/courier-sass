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
    public Task<string?> GetUserNameAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

public class ShipmentServiceNdrRtoTransitionTests
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

    private static async Task<(ShipmentService Service, Guid ShipmentId, Guid OriginBranchId, Guid DestinationBranchId)> SeedShipmentAtStatusAsync(
        TestDbContext dbContext, Guid organizationId, ShipmentStatus targetStatus)
    {
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;

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
        var actingUser = ActingUser(Guid.NewGuid());
        var riderId = Guid.NewGuid();

        if (targetStatus == ShipmentStatus.Created) return (service, created.Id, originBranchId, destinationBranchId);
        await service.AssignPickupToRiderAsync(created.Id, riderId, actingUser);
        if (targetStatus == ShipmentStatus.PickupAssigned) return (service, created.Id, originBranchId, destinationBranchId);
        await service.RiderPickupScanAsync(created.Id, riderId, actingUser);
        await service.ReceiveAtOriginAsync(created.Id, actingUser);
        await service.SortAsync(created.Id, actingUser);
        await service.DispatchManyAsync(new[] { created.Id }, actingUser);
        await service.ReceiveAtDestinationAsync(created.Id, actingUser);
        await service.AssignDeliveryToRiderAsync(created.Id, riderId, actingUser);
        if (targetStatus == ShipmentStatus.OutForDelivery) return (service, created.Id, originBranchId, destinationBranchId);
        await service.FailDeliveryAsync(created.Id, riderId, actingUser);
        if (targetStatus == ShipmentStatus.DeliveryFailed) return (service, created.Id, originBranchId, destinationBranchId);
        await service.TransitionToNdrCreatedAsync(created.Id, actingUser);
        if (targetStatus == ShipmentStatus.NdrCreated) return (service, created.Id, originBranchId, destinationBranchId);
        await service.InitiateRtoAsync(created.Id, actingUser);
        if (targetStatus == ShipmentStatus.RtoInitiated) return (service, created.Id, originBranchId, destinationBranchId);
        await service.DispatchManyRtoAsync(new[] { created.Id }, actingUser);
        if (targetStatus == ShipmentStatus.RtoInTransit) return (service, created.Id, originBranchId, destinationBranchId);
        await service.ReceiveRtoAtOriginAsync(created.Id, actingUser);
        return (service, created.Id, originBranchId, destinationBranchId);
    }

    [Fact]
    public async Task TransitionToNdrCreatedAsync_FromDeliveryFailed_Succeeds()
    {
        await using var dbContext = CreateContext();
        var (service, shipmentId, _, _) = await SeedShipmentAtStatusAsync(dbContext, Guid.NewGuid(), ShipmentStatus.DeliveryFailed);

        var result = await service.TransitionToNdrCreatedAsync(shipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.NdrCreated, result!.CurrentStatus);
    }

    [Fact]
    public async Task ReattemptDeliveryAsync_FromNdrCreated_TransitionsToOutForDeliveryWithoutTouchingCustody()
    {
        await using var dbContext = CreateContext();
        var (service, shipmentId, _, _) = await SeedShipmentAtStatusAsync(dbContext, Guid.NewGuid(), ShipmentStatus.NdrCreated);
        var beforeCustody = (await dbContext.Set<Shipment>().SingleAsync(s => s.Id == shipmentId)).CurrentCustodianRiderId;

        var result = await service.ReattemptDeliveryAsync(shipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.OutForDelivery, result!.CurrentStatus);
        Assert.Equal(beforeCustody, result.CurrentCustodianRiderId);
        Assert.NotNull(beforeCustody);
    }

    [Fact]
    public async Task InitiateRtoAsync_FromNdrCreated_MovesCustodyToDestinationBranch()
    {
        await using var dbContext = CreateContext();
        var (service, shipmentId, _, destinationBranchId) = await SeedShipmentAtStatusAsync(dbContext, Guid.NewGuid(), ShipmentStatus.NdrCreated);

        var result = await service.InitiateRtoAsync(shipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.RtoInitiated, result!.CurrentStatus);
        Assert.Null(result.CurrentCustodianRiderId);
        Assert.Equal(destinationBranchId, result.CurrentBranchId);
    }

    [Fact]
    public async Task InitiateRtoAsync_FromDeliveryFailed_ThrowsInvalidTransition()
    {
        // Regression test for CLARIFICATIONS.md #4: RTO_INITIATED must only ever be reachable from
        // NDR_CREATED, never directly from DELIVERY_FAILED.
        await using var dbContext = CreateContext();
        var (service, shipmentId, _, _) = await SeedShipmentAtStatusAsync(dbContext, Guid.NewGuid(), ShipmentStatus.DeliveryFailed);

        var ex = await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => service.InitiateRtoAsync(shipmentId, ActingUser(Guid.NewGuid())));
        Assert.Equal(ShipmentStatus.DeliveryFailed, ex.From);
        Assert.Equal(ShipmentStatus.RtoInitiated, ex.To);
    }

    [Fact]
    public async Task FullRtoPath_NdrCreatedToRtoDelivered_WalksEveryTransition()
    {
        await using var dbContext = CreateContext();
        var (service, shipmentId, originBranchId, destinationBranchId) = await SeedShipmentAtStatusAsync(dbContext, Guid.NewGuid(), ShipmentStatus.NdrCreated);
        var actingUser = ActingUser(Guid.NewGuid());

        var afterInitiate = await service.InitiateRtoAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.RtoInitiated, afterInitiate!.CurrentStatus);
        Assert.Equal(destinationBranchId, afterInitiate.CurrentBranchId);

        await service.DispatchManyRtoAsync(new[] { shipmentId }, actingUser);
        var afterDispatch = await dbContext.Set<Shipment>().SingleAsync(s => s.Id == shipmentId);
        Assert.Equal(ShipmentStatus.RtoInTransit, afterDispatch.CurrentStatus);
        Assert.Null(afterDispatch.CurrentBranchId);

        var afterReceive = await service.ReceiveRtoAtOriginAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.RtoAtOrigin, afterReceive!.CurrentStatus);
        Assert.Equal(originBranchId, afterReceive.CurrentBranchId);

        var afterConfirm = await service.ConfirmRtoDeliveredAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.RtoDelivered, afterConfirm!.CurrentStatus);

        var events = await dbContext.Set<ShipmentEvent>().Where(e => e.ShipmentId == shipmentId).OrderBy(e => e.Timestamp).Select(e => e.EventType).ToListAsync();
        Assert.Equal(new[]
        {
            ShipmentEventType.Created, ShipmentEventType.PickupAssigned, ShipmentEventType.PickedUp, ShipmentEventType.ReceivedAtOrigin,
            ShipmentEventType.Sorted, ShipmentEventType.Dispatched, ShipmentEventType.ReceivedAtDestination, ShipmentEventType.OutForDelivery,
            ShipmentEventType.DeliveryFailed, ShipmentEventType.NdrCreated, ShipmentEventType.RtoInitiated, ShipmentEventType.RtoInTransit,
            ShipmentEventType.RtoAtOrigin, ShipmentEventType.RtoDelivered
        }, events);
    }
}
