using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
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

// Not file-scoped: returned from SeedAsync's tuple (same reasoning as
// RiderServiceTasksTests.cs's AttemptsFakeShipmentTransitionService).
class AttemptsFakeShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();
    public List<Guid> FailCalls { get; } = new();
    public List<Guid> CompleteCalls { get; } = new();

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.TryGetValue(shipmentId, out var info) ? info : null);
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        CompleteCalls.Add(shipmentId);
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.Delivered, CurrentCustodianRiderId = null };
        return Task.FromResult<ShipmentResponse?>(null);
    }

    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        FailCalls.Add(shipmentId);
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.DeliveryFailed };
        return Task.FromResult<ShipmentResponse?>(null);
    }

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
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

// Not file-scoped: returned from SeedAsync's tuple.
class RecordingObjectStorage : IObjectStorage
{
    public List<string> UploadedKeyPrefixes { get; } = new();
    public Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        UploadedKeyPrefixes.Add(keyPrefix);
        return Task.FromResult($"{keyPrefix}/{Guid.NewGuid():N}");
    }
    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class RiderServiceAttemptsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RiderConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryTaskConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryRunConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryAttemptConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryProofConfiguration());
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

    private static async Task<(RiderService Service, AttemptsFakeShipmentTransitionService Shipments, RecordingObjectStorage Storage, Guid RiderId, Guid OrganizationId)> SeedAsync(TestDbContext dbContext)
    {
        var organizationId = Guid.NewGuid();
        var rider = new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = Guid.NewGuid(), BaseBranchId = Guid.NewGuid(), LicenseNumber = "LIC-001", RoleType = RiderRoleType.DeliveryRider, IsActive = true };
        dbContext.Set<Rider>().Add(rider);
        await dbContext.SaveChangesAsync();

        var shipments = new AttemptsFakeShipmentTransitionService();
        var storage = new RecordingObjectStorage();
        var service = new RiderService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), new AllBranchesAuthorizationChecker(), shipments, storage);
        return (service, shipments, storage, rider.Id, organizationId);
    }

    private static ShipmentScanInfo OutForDeliveryInfo(Guid riderId, string awb = "AWB001") => new(
        Guid.NewGuid(), Guid.NewGuid(), awb, ShipmentStatus.OutForDelivery, null, Guid.NewGuid(), Guid.NewGuid(), riderId);

    [Fact]
    public async Task RecordFailedAttemptAsync_ValidRequest_CreatesFailedAttemptAndFailsTask()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, _, riderId, _) = await SeedAsync(dbContext);
        var info = OutForDeliveryInfo(riderId);
        shipments.ShipmentsById[info.ShipmentId] = info;
        dbContext.Set<DeliveryTask>().Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = Guid.NewGuid(), ShipmentId = info.ShipmentId, Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Pending });
        await dbContext.SaveChangesAsync();

        var result = await service.RecordFailedAttemptAsync(riderId,
            new RecordFailedAttemptRequest(info.ShipmentId, DeliveryAttemptReason.CustomerUnavailable, "Not home", null, null, true, "Retry tomorrow"),
            ActingUser(Guid.NewGuid()));

        Assert.False(result.Succeeded);
        Assert.Equal(DeliveryAttemptReason.CustomerUnavailable, result.Reason);
        Assert.Equal(1, result.AttemptNumber);
        Assert.Single(shipments.FailCalls);
        var task = await dbContext.Set<DeliveryTask>().SingleAsync(t => t.ShipmentId == info.ShipmentId);
        Assert.Equal(DeliveryTaskStatus.Failed, task.Status);
    }

    [Fact]
    public async Task RecordFailedAttemptAsync_ShipmentNotAssignedToThisRider_ThrowsRiderValidationException()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, _, riderId, _) = await SeedAsync(dbContext);
        var info = OutForDeliveryInfo(Guid.NewGuid());
        shipments.ShipmentsById[info.ShipmentId] = info;

        await Assert.ThrowsAsync<RiderValidationException>(() => service.RecordFailedAttemptAsync(riderId,
            new RecordFailedAttemptRequest(info.ShipmentId, DeliveryAttemptReason.Other, null, null, null, false, null),
            ActingUser(Guid.NewGuid())));
        Assert.Empty(shipments.FailCalls);
    }

    [Fact]
    public async Task CompleteDeliveryAsync_ValidRequest_CreatesProofAndCompletesTask()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, storage, riderId, _) = await SeedAsync(dbContext);
        var info = OutForDeliveryInfo(riderId);
        shipments.ShipmentsById[info.ShipmentId] = info;
        dbContext.Set<DeliveryTask>().Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = Guid.NewGuid(), ShipmentId = info.ShipmentId, Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Pending });
        await dbContext.SaveChangesAsync();
        var signatureKey = (await service.UploadPodFileAsync(riderId, PodFileKind.Signature, new MemoryStream(new byte[] { 1, 2, 3 }), "image/png", ActingUser(Guid.NewGuid()))).StorageKey;

        var result = await service.CompleteDeliveryAsync(riderId,
            new CompleteDeliveryRequest(info.ShipmentId, "Jane Doe", true, signatureKey, null, null, null),
            ActingUser(Guid.NewGuid()));

        Assert.True(result.Succeeded);
        Assert.Null(result.Reason);
        Assert.Single(shipments.CompleteCalls);
        Assert.Single(storage.UploadedKeyPrefixes);
        var proof = await dbContext.Set<DeliveryProof>().SingleAsync(p => p.ShipmentId == info.ShipmentId);
        Assert.True(proof.OtpVerified);
        Assert.Equal(signatureKey, proof.SignatureStorageKey);
        Assert.Equal("Jane Doe", proof.RecipientName);
        var task = await dbContext.Set<DeliveryTask>().SingleAsync(t => t.ShipmentId == info.ShipmentId);
        Assert.Equal(DeliveryTaskStatus.Completed, task.Status);
    }

    [Fact]
    public async Task RecordFailedAttemptAsync_SecondAttemptOnSameShipment_IncrementsAttemptNumber()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, _, riderId, organizationId) = await SeedAsync(dbContext);
        var info = OutForDeliveryInfo(riderId);
        shipments.ShipmentsById[info.ShipmentId] = info;
        // Simulate a prior attempt already on record (a real second OutForDelivery attempt needs
        // Phase 9's reattempt loop, out of this phase's scope — this proves the counting logic
        // alone, independent of the state machine).
        dbContext.Set<DeliveryAttempt>().Add(new DeliveryAttempt { Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = info.ShipmentId, RiderId = riderId, AttemptNumber = 1, AttemptedAt = DateTimeOffset.UtcNow, Succeeded = false, Reason = DeliveryAttemptReason.Other, CustomerContacted = false });
        await dbContext.SaveChangesAsync();

        var result = await service.RecordFailedAttemptAsync(riderId,
            new RecordFailedAttemptRequest(info.ShipmentId, DeliveryAttemptReason.WrongAddress, null, null, null, false, null),
            ActingUser(Guid.NewGuid()));

        Assert.Equal(2, result.AttemptNumber);
    }
}
