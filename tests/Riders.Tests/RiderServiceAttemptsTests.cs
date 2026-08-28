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

// Not file-scoped: returned from SeedAsync's tuple (same reasoning as
// RiderServiceTasksTests.cs's AttemptsFakeShipmentTransitionService).
class AttemptsFakeShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();
    public Dictionary<Guid, Guid> MerchantIdByShipmentId { get; } = new();
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
        var merchantId = MerchantIdByShipmentId.TryGetValue(shipmentId, out var m) ? m : Guid.NewGuid();
        var response = new ShipmentResponse(
            shipmentId, "TESTAWB1", merchantId, Guid.NewGuid(), Guid.NewGuid(), null, null, null,
            ShipmentStatus.Delivered, Guid.NewGuid(), 1m, null, null, null, 100m, 0m, false, 100m, 0m, 0m, 100m,
            recipientName, "9800000000", "Address", "Sender", "9800000001", "Address", new List<ShipmentItemResponse>(), DateTimeOffset.UtcNow);
        return Task.FromResult<ShipmentResponse?>(response);
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
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

// Not file-scoped: returned from SeedAsync's tuple.
class RecordingNdrCreationService : INdrCreationService
{
    public List<(Guid ShipmentId, Guid OrganizationId, NdrReason Reason)> Calls { get; } = new();
    public Task CreateFromFailedAttemptAsync(Guid shipmentId, Guid organizationId, NdrReason reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        Calls.Add((shipmentId, organizationId, reason));
        return Task.CompletedTask;
    }
}

// Not file-scoped (unlike a plain no-op fake would be): returned from SeedAsync's tuple, same
// reasoning as RecordingObjectStorage/RecordingNdrCreationService above — a file-local type cannot
// appear in a non-file-local member signature (CS9051).
class RecordingCodCollectionService : ICodCollectionService
{
    public List<(Guid ShipmentId, decimal CollectedAmount, Guid RiderId, Guid DeliveryProofId)> Calls { get; } = new();

    public Task MarkCollectedAsync(Guid shipmentId, decimal collectedAmount, Guid riderId, Guid deliveryProofId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        Calls.Add((shipmentId, collectedAmount, riderId, deliveryProofId));
        return Task.CompletedTask;
    }
}

// Not file-scoped: returned from SeedAsync's tuple (same reasoning as RecordingCodCollectionService).
class ConfigurableCodPermissionChecker : IPermissionChecker
{
    public bool GrantCodCollect { get; set; } = true;
    public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(permission != "cod.collect" || GrantCodCollect);
}

// Not file-scoped: returned from SeedAsync's tuple, same reasoning as the other recording fakes
// above.
class RecordingOutboxWriter : IOutboxWriter
{
    public List<(Guid OrganizationId, string EventType, object Payload)> Writes { get; } = new();
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default)
    {
        Writes.Add((organizationId, eventType, payload));
        return Task.CompletedTask;
    }
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

    private static async Task<(RiderService Service, AttemptsFakeShipmentTransitionService Shipments, RecordingObjectStorage Storage, RecordingNdrCreationService Ndr, RecordingCodCollectionService Cod, ConfigurableCodPermissionChecker PermissionChecker, Guid RiderId, Guid OrganizationId, RecordingOutboxWriter OutboxWriter)> SeedAsync(TestDbContext dbContext)
    {
        var organizationId = Guid.NewGuid();
        var rider = new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = Guid.NewGuid(), BaseBranchId = Guid.NewGuid(), LicenseNumber = "LIC-001", RoleType = RiderRoleType.DeliveryRider, IsActive = true };
        dbContext.Set<Rider>().Add(rider);
        await dbContext.SaveChangesAsync();

        var shipments = new AttemptsFakeShipmentTransitionService();
        var storage = new RecordingObjectStorage();
        var ndr = new RecordingNdrCreationService();
        var cod = new RecordingCodCollectionService();
        var permissionChecker = new ConfigurableCodPermissionChecker();
        var outboxWriter = new RecordingOutboxWriter();
        var service = new RiderService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), new AllBranchesAuthorizationChecker(), shipments, storage, ndr, cod, permissionChecker, outboxWriter);
        return (service, shipments, storage, ndr, cod, permissionChecker, rider.Id, organizationId, outboxWriter);
    }

    // Thin wrapper around SeedAsync that additionally seeds an OutForDelivery shipment carrying
    // codAmount and a pending DeliveryTask, mirroring the setup CompleteDeliveryAsync_ValidRequest_
    // CreatesProofAndCompletesTask performs inline. Owns its own DbContext (not disposed — same
    // in-memory-provider tradeoff already implicit elsewhere in this file) so callers can
    // destructure the result directly without a separate `await using var dbContext` line.
    private static async Task<(RiderService Service, Guid RiderId, Guid ShipmentId, RecordingCodCollectionService CodRecorder)> SeedCodShipmentOutForDeliveryAsync(decimal codAmount)
    {
        var dbContext = CreateContext();
        var seed = await SeedAsync(dbContext);
        var info = OutForDeliveryInfo(seed.RiderId) with { CodAmount = codAmount };
        seed.Shipments.ShipmentsById[info.ShipmentId] = info;
        dbContext.Set<DeliveryTask>().Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = Guid.NewGuid(), ShipmentId = info.ShipmentId, Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Pending });
        await dbContext.SaveChangesAsync();
        return (seed.Service, seed.RiderId, info.ShipmentId, seed.Cod);
    }

    // Same as SeedCodShipmentOutForDeliveryAsync, but also returns the ConfigurableCodPermissionChecker
    // so a test can flip GrantCodCollect off.
    private static async Task<(RiderService Service, Guid RiderId, Guid ShipmentId, RecordingCodCollectionService CodRecorder, ConfigurableCodPermissionChecker PermissionChecker)> SeedCodShipmentOutForDeliveryAsyncWithPermissionChecker(decimal codAmount)
    {
        var dbContext = CreateContext();
        var seed = await SeedAsync(dbContext);
        var info = OutForDeliveryInfo(seed.RiderId) with { CodAmount = codAmount };
        seed.Shipments.ShipmentsById[info.ShipmentId] = info;
        dbContext.Set<DeliveryTask>().Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = Guid.NewGuid(), ShipmentId = info.ShipmentId, Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Pending });
        await dbContext.SaveChangesAsync();
        return (seed.Service, seed.RiderId, info.ShipmentId, seed.Cod, seed.PermissionChecker);
    }

    private static ShipmentScanInfo OutForDeliveryInfo(Guid riderId, string awb = "AWB001") => new(
        Guid.NewGuid(), Guid.NewGuid(), awb, ShipmentStatus.OutForDelivery, null, Guid.NewGuid(), Guid.NewGuid(), riderId);

    [Fact]
    public async Task RecordFailedAttemptAsync_ValidRequest_CreatesFailedAttemptAndFailsTask()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, _, _, _, _, riderId, _, _) = await SeedAsync(dbContext);
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
        var (service, shipments, _, _, _, _, riderId, _, _) = await SeedAsync(dbContext);
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
        var (service, shipments, storage, _, _, _, riderId, _, _) = await SeedAsync(dbContext);
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
    public async Task CompleteDeliveryAsync_WritesShipmentDeliveredOutboxMessage()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, _, _, _, _, riderId, organizationId, outboxWriter) = await SeedAsync(dbContext);
        var info = OutForDeliveryInfo(riderId);
        var merchantId = Guid.NewGuid();
        shipments.ShipmentsById[info.ShipmentId] = info;
        shipments.MerchantIdByShipmentId[info.ShipmentId] = merchantId;
        dbContext.Set<DeliveryTask>().Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = Guid.NewGuid(), ShipmentId = info.ShipmentId, Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Pending });
        await dbContext.SaveChangesAsync();

        await service.CompleteDeliveryAsync(riderId,
            new CompleteDeliveryRequest(info.ShipmentId, "Jane Doe", true, null, null, null, null),
            ActingUser(Guid.NewGuid()));

        Assert.Single(outboxWriter.Writes, w => w.EventType == "shipment.delivered" && w.OrganizationId == organizationId);
    }

    [Fact]
    public async Task RecordFailedAttemptAsync_SecondAttemptOnSameShipment_IncrementsAttemptNumber()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, _, _, _, _, riderId, organizationId, _) = await SeedAsync(dbContext);
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

    [Fact]
    public async Task RecordFailedAttemptAsync_ValidRequest_CreatesOpenNdrWithMappedReason()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, _, ndr, _, _, riderId, organizationId, _) = await SeedAsync(dbContext);
        var info = OutForDeliveryInfo(riderId);
        shipments.ShipmentsById[info.ShipmentId] = info;

        await service.RecordFailedAttemptAsync(riderId,
            new RecordFailedAttemptRequest(info.ShipmentId, DeliveryAttemptReason.WrongAddress, null, null, null, false, null),
            ActingUser(Guid.NewGuid()));

        var call = Assert.Single(ndr.Calls);
        Assert.Equal(info.ShipmentId, call.ShipmentId);
        Assert.Equal(organizationId, call.OrganizationId);
        Assert.Equal(NdrReason.WrongAddress, call.Reason);
    }

    [Fact]
    public async Task CompleteDeliveryAsync_ValidRequest_NeverCreatesAnNdr()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, _, ndr, _, _, riderId, _, _) = await SeedAsync(dbContext);
        var info = OutForDeliveryInfo(riderId);
        shipments.ShipmentsById[info.ShipmentId] = info;
        dbContext.Set<DeliveryTask>().Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = Guid.NewGuid(), ShipmentId = info.ShipmentId, Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Pending });
        await dbContext.SaveChangesAsync();

        await service.CompleteDeliveryAsync(riderId,
            new CompleteDeliveryRequest(info.ShipmentId, "Jane Doe", true, null, null, null, null),
            ActingUser(Guid.NewGuid()));

        Assert.Empty(ndr.Calls);
    }

    // Named for what this test actually asserts (ShipmentId, CollectedAmount, RiderId) — that
    // MarkCollectedAsync is also called with the just-created DeliveryProof's Id is verified by code
    // inspection (CompleteDeliveryAsync passes proof.Id, not a separate/stale value) rather than by
    // an assertion here, since the created proof's Id isn't independently observable from this test.
    [Fact]
    public async Task CompleteDeliveryAsync_CodShipmentWithAmount_CallsMarkCollectedWithShipmentAmountAndRider()
    {
        var (service, riderId, shipmentId, codRecorder) = await SeedCodShipmentOutForDeliveryAsync(2500m);

        var result = await service.CompleteDeliveryAsync(riderId, new CompleteDeliveryRequest(shipmentId, "Recipient", true, null, null, null, null, 2500m), ActingUser(Guid.NewGuid()));

        var call = Assert.Single(codRecorder.Calls);
        Assert.Equal(shipmentId, call.ShipmentId);
        Assert.Equal(2500m, call.CollectedAmount);
        Assert.Equal(riderId, call.RiderId);
    }

    [Fact]
    public async Task CompleteDeliveryAsync_CodShipmentWithoutCollectedAmount_Throws()
    {
        var (service, riderId, shipmentId, _) = await SeedCodShipmentOutForDeliveryAsync(2500m);

        await Assert.ThrowsAsync<RiderValidationException>(() =>
            service.CompleteDeliveryAsync(riderId, new CompleteDeliveryRequest(shipmentId, "Recipient", true, null, null, null, null, null), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CompleteDeliveryAsync_CodShipmentWithNegativeCollectedAmount_Throws()
    {
        var (service, riderId, shipmentId, codRecorder) = await SeedCodShipmentOutForDeliveryAsync(2500m);

        await Assert.ThrowsAsync<RiderValidationException>(() =>
            service.CompleteDeliveryAsync(riderId, new CompleteDeliveryRequest(shipmentId, "Recipient", true, null, null, null, null, -100m), ActingUser(Guid.NewGuid())));
        Assert.Empty(codRecorder.Calls);
    }

    [Fact]
    public async Task CompleteDeliveryAsync_CodShipmentWithoutCodCollectPermission_Throws()
    {
        var (service, riderId, shipmentId, _, permissionChecker) = await SeedCodShipmentOutForDeliveryAsyncWithPermissionChecker(2500m);
        permissionChecker.GrantCodCollect = false;

        await Assert.ThrowsAsync<RiderValidationException>(() =>
            service.CompleteDeliveryAsync(riderId, new CompleteDeliveryRequest(shipmentId, "Recipient", true, null, null, null, null, 2500m), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CompleteDeliveryAsync_NonCodShipment_NeverCallsMarkCollected()
    {
        var (service, riderId, shipmentId, codRecorder) = await SeedCodShipmentOutForDeliveryAsync(0m);

        await service.CompleteDeliveryAsync(riderId, new CompleteDeliveryRequest(shipmentId, "Recipient", true, null, null, null, null, null), ActingUser(Guid.NewGuid()));

        Assert.Empty(codRecorder.Calls);
    }
}
