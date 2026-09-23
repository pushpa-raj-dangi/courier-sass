using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Events.Application;
using Courier.Identity.Application;
using Courier.Ndr.Application;
using Courier.Ndr.Domain;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.Riders.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Riders.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

// Not file-scoped: returned from SeedRiderAsync's tuple, same reasoning as
// RiderServiceCreationTests.cs's FakeUserProvisioningService.
class TasksFakeShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();
    public List<(Guid ShipmentId, Guid RiderId)> AssignPickupCalls { get; } = new();
    public List<(Guid ShipmentId, Guid RiderId)> PickupScanCalls { get; } = new();
    public List<(Guid ShipmentId, Guid RiderId)> AssignDeliveryCalls { get; } = new();

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.TryGetValue(shipmentId, out var info) ? info : null);
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.Values.FirstOrDefault(s => s.Awb == awb));
    public Task<ShipmentResponse?> GetResponseAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        AssignPickupCalls.Add((shipmentId, riderId));
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.PickupAssigned, CurrentCustodianRiderId = riderId };
        return Task.FromResult<ShipmentResponse?>(null);
    }

    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        PickupScanCalls.Add((shipmentId, riderId));
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.PickedUp };
        return Task.FromResult<ShipmentResponse?>(new ShipmentResponse(
            shipmentId, info.Awb, Guid.NewGuid(), info.OriginBranchId, info.DestinationBranchId, null, null, riderId,
            ShipmentStatus.PickedUp, Guid.NewGuid(), 1m, null, null, null, 100m, 0m, false, 100m, 0m, 0m, 100m,
            "Recipient", "9800000000", "Address", "Sender", "9800000001", "Address", new List<ShipmentItemResponse>(), DateTimeOffset.UtcNow));
    }

    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        AssignDeliveryCalls.Add((shipmentId, riderId));
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.OutForDelivery, CurrentCustodianRiderId = riderId, CurrentBranchId = null };
        return Task.FromResult<ShipmentResponse?>(null);
    }

    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class NotSupportedUserProvisioningService : IUserProvisioningService
{
    public Task<Guid> ProvisionStaffUserAsync(ProvisionStaffUserRequest request, Guid organizationId, Guid grantedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class NotSupportedBranchLookup : IBranchLookupService
{
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class NotSupportedObjectStorage : IObjectStorage
{
    public Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class NotSupportedNdrCreationService : INdrCreationService
{
    public Task CreateFromFailedAttemptAsync(Guid shipmentId, Guid organizationId, NdrReason reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class NoopCodCollectionService : ICodCollectionService
{
    public Task MarkCollectedAsync(Guid shipmentId, decimal collectedAmount, Guid riderId, Guid deliveryProofId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class AlwaysTruePermissionChecker : IPermissionChecker
{
    public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file class NoopOutboxWriter : IOutboxWriter
{
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class NotSupportedShipmentTaskLookupService : IShipmentTaskLookupService
{
    public Task<ShipmentTaskInfo?> GetTaskInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class RiderServiceTasksTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RiderConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryRunConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryTaskConfiguration());
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

    private static async Task<(RiderService Service, TasksFakeShipmentTransitionService Shipments, Guid RiderId)> SeedRiderAsync(TestDbContext dbContext, Guid organizationId, Guid branchId)
    {
        var rider = new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = Guid.NewGuid(), BaseBranchId = branchId, LicenseNumber = "LIC-001", RoleType = RiderRoleType.PickupRider, IsActive = true };
        dbContext.Set<Rider>().Add(rider);
        await dbContext.SaveChangesAsync();

        var shipments = new TasksFakeShipmentTransitionService();
        var service = new RiderService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), new AllBranchesAuthorizationChecker(), shipments, new NotSupportedObjectStorage(), new NotSupportedNdrCreationService(),
            new NoopCodCollectionService(), new AlwaysTruePermissionChecker(), new NoopOutboxWriter(), new NotSupportedShipmentTaskLookupService());
        return (service, shipments, rider.Id);
    }

    private static ShipmentScanInfo CreatedInfo(Guid originBranchId, Guid destinationBranchId, string awb = "AWB001") => new(
        Guid.NewGuid(), Guid.NewGuid(), awb, ShipmentStatus.Created, null, originBranchId, destinationBranchId, null);

    [Fact]
    public async Task AssignTaskAsync_TwoPickupTasksSameRiderSameDay_ReuseSameRunWithIncrementingSequence()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var (service, shipments, riderId) = await SeedRiderAsync(dbContext, organizationId, branchId);
        var info1 = CreatedInfo(branchId, Guid.NewGuid(), "AWB001");
        var info2 = CreatedInfo(branchId, Guid.NewGuid(), "AWB002");
        shipments.ShipmentsById[info1.ShipmentId] = info1;
        shipments.ShipmentsById[info2.ShipmentId] = info2;

        var task1 = await service.AssignTaskAsync(new AssignDeliveryTaskRequest(riderId, info1.ShipmentId, DeliveryTaskType.Pickup), ActingUser(Guid.NewGuid()));
        var task2 = await service.AssignTaskAsync(new AssignDeliveryTaskRequest(riderId, info2.ShipmentId, DeliveryTaskType.Pickup), ActingUser(Guid.NewGuid()));

        Assert.Equal(task1.DeliveryRunId, task2.DeliveryRunId);
        Assert.Equal(1, task1.Sequence);
        Assert.Equal(2, task2.Sequence);
        var runs = await dbContext.Set<DeliveryRun>().ToListAsync();
        Assert.Single(runs);
        Assert.Equal(2, shipments.AssignPickupCalls.Count);
    }

    [Fact]
    public async Task AssignTaskAsync_ShipmentNotCreated_ThrowsRiderValidationException()
    {
        await using var dbContext = CreateContext();
        var branchId = Guid.NewGuid();
        var (service, shipments, riderId) = await SeedRiderAsync(dbContext, Guid.NewGuid(), branchId);
        var info = CreatedInfo(branchId, Guid.NewGuid()) with { CurrentStatus = ShipmentStatus.PickedUp };
        shipments.ShipmentsById[info.ShipmentId] = info;

        await Assert.ThrowsAsync<RiderValidationException>(() => service.AssignTaskAsync(
            new AssignDeliveryTaskRequest(riderId, info.ShipmentId, DeliveryTaskType.Pickup), ActingUser(Guid.NewGuid())));
        Assert.Empty(shipments.AssignPickupCalls);
    }

    [Fact]
    public async Task RiderPickupScanAsync_AssignedToDifferentRider_ThrowsRiderValidationException()
    {
        await using var dbContext = CreateContext();
        var branchId = Guid.NewGuid();
        var (service, shipments, riderId) = await SeedRiderAsync(dbContext, Guid.NewGuid(), branchId);
        var info = CreatedInfo(branchId, Guid.NewGuid()) with { CurrentStatus = ShipmentStatus.PickupAssigned, CurrentCustodianRiderId = Guid.NewGuid() };
        shipments.ShipmentsById[info.ShipmentId] = info;

        var ex = await Assert.ThrowsAsync<RiderValidationException>(() => service.RiderPickupScanAsync(
            riderId, new RiderScanRequest(info.Awb), ActingUser(Guid.NewGuid())));
        Assert.Contains("not assigned to this rider", ex.Message);
    }

    [Fact]
    public async Task RiderPickupScanAsync_ValidScan_CompletesPickupTask()
    {
        await using var dbContext = CreateContext();
        var branchId = Guid.NewGuid();
        var (service, shipments, riderId) = await SeedRiderAsync(dbContext, Guid.NewGuid(), branchId);
        var info = CreatedInfo(branchId, Guid.NewGuid());
        shipments.ShipmentsById[info.ShipmentId] = info;
        await service.AssignTaskAsync(new AssignDeliveryTaskRequest(riderId, info.ShipmentId, DeliveryTaskType.Pickup), ActingUser(Guid.NewGuid()));

        await service.RiderPickupScanAsync(riderId, new RiderScanRequest(info.Awb), ActingUser(Guid.NewGuid()));

        var task = await dbContext.Set<DeliveryTask>().SingleAsync(t => t.ShipmentId == info.ShipmentId);
        Assert.Equal(DeliveryTaskStatus.Completed, task.Status);
    }
}
