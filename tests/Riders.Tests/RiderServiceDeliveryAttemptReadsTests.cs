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

file class ScopedBranchesChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid>? _accessible;
    public ScopedBranchesChecker(IReadOnlySet<Guid>? accessible) => _accessible = accessible;
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessible);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessible is null || _accessible.Contains(branchId));
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

file class ScanInfoShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();
    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.TryGetValue(shipmentId, out var info) ? info : null);
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

file class NotSupportedShipmentTaskLookupService : IShipmentTaskLookupService
{
    public Task<ShipmentTaskInfo?> GetTaskInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class RiderServiceDeliveryAttemptReadsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RiderConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryAttemptConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal ActingUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    private static RiderService CreateService(TestDbContext dbContext, Guid organizationId, IReadOnlySet<Guid>? accessible, IShipmentTransitionService shipments) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), new ScopedBranchesChecker(accessible), shipments, new NotSupportedObjectStorage(),
            new NotSupportedNdrCreationService(), new NoopCodCollectionService(), new AlwaysTruePermissionChecker(),
            new NoopOutboxWriter(), new NotSupportedShipmentTaskLookupService());

    private static ShipmentScanInfo ScanInfo(Guid shipmentId, Guid organizationId, Guid origin, Guid destination, Guid? current) =>
        new(shipmentId, organizationId, "AWB-1", ShipmentStatus.NdrCreated, current, origin, destination, null);

    private static DeliveryAttempt Attempt(Guid organizationId, Guid shipmentId, int number, DateTimeOffset at, bool succeeded = false) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = shipmentId, RiderId = Guid.NewGuid(),
        AttemptNumber = number, AttemptedAt = at, Succeeded = succeeded,
        Reason = succeeded ? null : DeliveryAttemptReason.CustomerUnavailable,
        Notes = $"attempt {number}", CustomerContacted = true, NextAction = "Reattempt", Latitude = 27.7m, Longitude = 85.3m
    };

    [Fact]
    public async Task ListDeliveryAttemptsAsync_ReturnsThisShipmentsAttemptsNewestFirst()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow.AddDays(-1);
        dbContext.Set<DeliveryAttempt>().AddRange(
            Attempt(organizationId, shipmentId, 1, at),
            Attempt(organizationId, shipmentId, 3, at.AddHours(5)),
            Attempt(organizationId, shipmentId, 2, at.AddHours(5)),
            Attempt(organizationId, Guid.NewGuid(), 1, at.AddHours(9)));
        await dbContext.SaveChangesAsync();
        var shipments = new ScanInfoShipmentTransitionService();
        shipments.ShipmentsById[shipmentId] = ScanInfo(shipmentId, organizationId, Guid.NewGuid(), Guid.NewGuid(), null);
        var service = CreateService(dbContext, organizationId, null, shipments);

        var result = await service.ListDeliveryAttemptsAsync(shipmentId, ActingUser());

        Assert.NotNull(result);
        Assert.Equal(new[] { 3, 2, 1 }, result!.Items.Select(a => a.AttemptNumber).ToArray());
        var newest = result.Items[0];
        Assert.False(newest.Succeeded);
        Assert.Equal(DeliveryAttemptReason.CustomerUnavailable, newest.Reason);
        Assert.Equal("attempt 3", newest.Notes);
        Assert.True(newest.CustomerContacted);
        Assert.Equal("Reattempt", newest.NextAction);
    }

    [Fact]
    public async Task ListDeliveryAttemptsAsync_ShipmentOutsideNdrReadScope_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var shipments = new ScanInfoShipmentTransitionService();
        shipments.ShipmentsById[shipmentId] = ScanInfo(shipmentId, organizationId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new HashSet<Guid> { Guid.NewGuid() }, shipments);

        Assert.Null(await service.ListDeliveryAttemptsAsync(shipmentId, ActingUser()));
    }

    [Fact]
    public async Task ListDeliveryAttemptsAsync_InScopeThroughCurrentBranchOnly_ReturnsItems()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var currentBranchId = Guid.NewGuid();
        dbContext.Set<DeliveryAttempt>().Add(Attempt(organizationId, shipmentId, 1, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();
        var shipments = new ScanInfoShipmentTransitionService();
        shipments.ShipmentsById[shipmentId] = ScanInfo(shipmentId, organizationId, Guid.NewGuid(), Guid.NewGuid(), currentBranchId);
        var service = CreateService(dbContext, organizationId, new HashSet<Guid> { currentBranchId }, shipments);

        var result = await service.ListDeliveryAttemptsAsync(shipmentId, ActingUser());

        Assert.Single(result!.Items);
    }

    [Fact]
    public async Task ListDeliveryAttemptsAsync_UnknownShipment_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid(), null, new ScanInfoShipmentTransitionService());

        Assert.Null(await service.ListDeliveryAttemptsAsync(Guid.NewGuid(), ActingUser()));
    }

    [Fact]
    public async Task ListDeliveryAttemptsAsync_NoAttemptsYet_ReturnsEmptyItems()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var shipments = new ScanInfoShipmentTransitionService();
        shipments.ShipmentsById[shipmentId] = ScanInfo(shipmentId, organizationId, Guid.NewGuid(), Guid.NewGuid(), null);
        var service = CreateService(dbContext, organizationId, null, shipments);

        var result = await service.ListDeliveryAttemptsAsync(shipmentId, ActingUser());

        Assert.NotNull(result);
        Assert.Empty(result!.Items);
    }
}
