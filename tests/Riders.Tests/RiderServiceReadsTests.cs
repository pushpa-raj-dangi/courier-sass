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

file class RestrictedBranchAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly HashSet<Guid> _accessibleBranchIds;
    public RestrictedBranchAuthorizationChecker(params Guid[] accessibleBranchIds) => _accessibleBranchIds = accessibleBranchIds.ToHashSet();
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(_accessibleBranchIds);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessibleBranchIds.Contains(branchId));
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

file class NotSupportedShipmentTransitionService : IShipmentTransitionService
{
    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> GetResponseAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

file class FakeShipmentTaskLookupService : IShipmentTaskLookupService
{
    private readonly Dictionary<Guid, ShipmentTaskInfo> _infoByShipmentId;
    public FakeShipmentTaskLookupService(Dictionary<Guid, ShipmentTaskInfo> infoByShipmentId) => _infoByShipmentId = infoByShipmentId;
    public Task<ShipmentTaskInfo?> GetTaskInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_infoByShipmentId.TryGetValue(shipmentId, out var info) ? info : null);
}

file class NotSupportedShipmentTaskLookupService : IShipmentTaskLookupService
{
    public Task<ShipmentTaskInfo?> GetTaskInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class RiderServiceReadsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RiderConfiguration());
            modelBuilder.ApplyConfiguration(new VehicleConfiguration());
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

    private static RiderService CreateService(TestDbContext dbContext, Guid organizationId, IBranchAuthorizationChecker checker, IShipmentTaskLookupService? shipmentTaskLookup = null) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), checker, new NotSupportedShipmentTransitionService(), new NotSupportedObjectStorage(), new NotSupportedNdrCreationService(),
            new NoopCodCollectionService(), new AlwaysTruePermissionChecker(), new NoopOutboxWriter(), shipmentTaskLookup ?? new NotSupportedShipmentTaskLookupService());

    [Fact]
    public async Task ListAsync_RestrictedToOneBranch_OnlyReturnsRidersAtThatBranch()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var visibleBranchId = Guid.NewGuid();
        var hiddenBranchId = Guid.NewGuid();
        dbContext.Set<Rider>().Add(new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = Guid.NewGuid(), BaseBranchId = visibleBranchId, LicenseNumber = "LIC-1", RoleType = RiderRoleType.PickupRider, IsActive = true });
        dbContext.Set<Rider>().Add(new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = Guid.NewGuid(), BaseBranchId = hiddenBranchId, LicenseNumber = "LIC-2", RoleType = RiderRoleType.PickupRider, IsActive = true });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker(visibleBranchId));

        var result = await service.ListAsync(ActingUser(Guid.NewGuid()));

        var rider = Assert.Single(result);
        Assert.Equal(visibleBranchId, rider.BaseBranchId);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid(), new RestrictedBranchAuthorizationChecker());

        var result = await service.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetMyRiderAsync_UserHasRiderRecord_ReturnsIt()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var rider = new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = userId, BaseBranchId = Guid.NewGuid(), LicenseNumber = "LIC-3", RoleType = RiderRoleType.DeliveryRider, IsActive = true };
        dbContext.Set<Rider>().Add(rider);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker());

        var result = await service.GetMyRiderAsync(ActingUser(userId));

        Assert.NotNull(result);
        Assert.Equal(rider.Id, result!.Id);
    }

    [Fact]
    public async Task GetMyRiderAsync_UserHasNoRiderRecord_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid(), new RestrictedBranchAuthorizationChecker());

        var result = await service.GetMyRiderAsync(ActingUser(Guid.NewGuid()));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetMyTasksTodayAsync_ActiveRunWithPendingTask_ReturnsEnrichedTask()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var rider = new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = userId, BaseBranchId = branchId, LicenseNumber = "LIC-4", RoleType = RiderRoleType.DeliveryRider, IsActive = true };
        dbContext.Set<Rider>().Add(rider);
        var run = new DeliveryRun { Id = Guid.NewGuid(), RiderId = rider.Id, BranchId = branchId, Date = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date), Status = DeliveryRunStatus.Active };
        dbContext.Set<DeliveryRun>().Add(run);
        var shipmentId = Guid.NewGuid();
        var task = new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = run.Id, ShipmentId = shipmentId, Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Pending };
        dbContext.Set<DeliveryTask>().Add(task);
        await dbContext.SaveChangesAsync();
        var taskInfo = new ShipmentTaskInfo("AWB001", "Recipient One", "9800000000", "Recipient Address", 3, 500m);
        var lookup = new FakeShipmentTaskLookupService(new() { [shipmentId] = taskInfo });
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker(), lookup);

        var result = await service.GetMyTasksTodayAsync(ActingUser(userId));

        Assert.Equal(run.Id, result.DeliveryRunId);
        var only = Assert.Single(result.Tasks);
        Assert.Equal(task.Id, only.TaskId);
        Assert.Equal("AWB001", only.Awb);
        Assert.Equal(500m, only.CodAmount);
    }

    [Fact]
    public async Task GetMyTasksTodayAsync_CompletedTaskExcluded_OnlyPendingReturned()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var rider = new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = userId, BaseBranchId = branchId, LicenseNumber = "LIC-5", RoleType = RiderRoleType.DeliveryRider, IsActive = true };
        dbContext.Set<Rider>().Add(rider);
        var run = new DeliveryRun { Id = Guid.NewGuid(), RiderId = rider.Id, BranchId = branchId, Date = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date), Status = DeliveryRunStatus.Active };
        dbContext.Set<DeliveryRun>().Add(run);
        dbContext.Set<DeliveryTask>().Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = run.Id, ShipmentId = Guid.NewGuid(), Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Completed });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker(), new FakeShipmentTaskLookupService(new()));

        var result = await service.GetMyTasksTodayAsync(ActingUser(userId));

        Assert.Equal(run.Id, result.DeliveryRunId);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public async Task GetMyTasksTodayAsync_NoActiveRunToday_ReturnsNullRunAndEmptyTasks()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var rider = new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = userId, BaseBranchId = Guid.NewGuid(), LicenseNumber = "LIC-6", RoleType = RiderRoleType.DeliveryRider, IsActive = true };
        dbContext.Set<Rider>().Add(rider);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker(), new FakeShipmentTaskLookupService(new()));

        var result = await service.GetMyTasksTodayAsync(ActingUser(userId));

        Assert.Null(result.DeliveryRunId);
        Assert.Empty(result.Tasks);
    }
}
