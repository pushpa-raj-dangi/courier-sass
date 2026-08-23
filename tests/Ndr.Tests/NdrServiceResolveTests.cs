using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Ndr.Application;
using Courier.Ndr.Domain;
using Courier.Ndr.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Ndr.Tests;

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

file class RecordingShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();
    public List<Guid> ReattemptCalls { get; } = new();
    public List<Guid> InitiateRtoCalls { get; } = new();

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.TryGetValue(shipmentId, out var info) ? info : null);
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        ReattemptCalls.Add(shipmentId);
        return Task.FromResult<ShipmentResponse?>(null);
    }

    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        InitiateRtoCalls.Add(shipmentId);
        return Task.FromResult<ShipmentResponse?>(null);
    }

    public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class NdrServiceResolveTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new NdrConfiguration());
            modelBuilder.ApplyConfiguration(new ReturnShipmentConfiguration());
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

    // Shipments is typed as the public IShipmentTransitionService, not the file-local
    // RecordingShipmentTransitionService, in this method's signature: RecordingShipmentTransitionService
    // is file-local (needed to avoid colliding with NdrCreationServiceTests.cs's own same-named,
    // non-file-local helper in this namespace), and CS9051 forbids exposing a file-local type through
    // a member signature of this non-file-local test class. Tests that need the recorded call lists
    // downcast the returned reference back to RecordingShipmentTransitionService in their own body,
    // which is unrestricted.
    private static async Task<(NDR Entity, NdrService Service, IShipmentTransitionService Shipments, Guid OriginBranchId, Guid DestinationBranchId)> SeedNdrAsync(
        TestDbContext dbContext, Guid organizationId, NdrStatus status)
    {
        var shipmentId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var ndr = new NDR { Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = shipmentId, Reason = NdrReason.CustomerUnavailable, Status = status };
        dbContext.Set<NDR>().Add(ndr);
        await dbContext.SaveChangesAsync();

        var shipments = new RecordingShipmentTransitionService();
        shipments.ShipmentsById[shipmentId] = new ShipmentScanInfo(shipmentId, organizationId, "AWB001", ShipmentStatus.NdrCreated, null, originBranchId, destinationBranchId, null);
        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new AllBranchesAuthorizationChecker());
        return (ndr, service, shipments, originBranchId, destinationBranchId);
    }

    [Fact]
    public async Task ResolveAsync_ReattemptFromReattemptScheduled_TransitionsShipmentAndResolvesNdr()
    {
        await using var dbContext = CreateContext();
        var (ndr, service, shipments, _, _) = await SeedNdrAsync(dbContext, Guid.NewGuid(), NdrStatus.ReattemptScheduled);

        var result = await service.ResolveAsync(ndr.Id, new ResolveNdrRequest(NdrResolution.Reattempt, null), ActingUser(Guid.NewGuid()));

        Assert.Equal(NdrStatus.Resolved, result!.Status);
        Assert.Equal(NdrResolution.Reattempt, result.Resolution);
        Assert.NotNull(result.ResolvedAt);
        Assert.Single(((RecordingShipmentTransitionService)shipments).ReattemptCalls, ndr.ShipmentId);
    }

    [Fact]
    public async Task ResolveAsync_ReattemptWhenNotReattemptScheduled_ThrowsNdrValidationException()
    {
        await using var dbContext = CreateContext();
        var (ndr, service, _, _, _) = await SeedNdrAsync(dbContext, Guid.NewGuid(), NdrStatus.Open);

        await Assert.ThrowsAsync<NdrValidationException>(() => service.ResolveAsync(ndr.Id, new ResolveNdrRequest(NdrResolution.Reattempt, null), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task ResolveAsync_RtoFromRtoRequested_CreatesReturnShipmentWithSwappedBranches()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (ndr, service, shipments, originBranchId, destinationBranchId) = await SeedNdrAsync(dbContext, organizationId, NdrStatus.RtoRequested);

        var result = await service.ResolveAsync(ndr.Id, new ResolveNdrRequest(NdrResolution.Rto, "Customer refused after three contacts"), ActingUser(Guid.NewGuid()));

        Assert.Equal(NdrResolution.Rto, result!.Resolution);
        Assert.Single(((RecordingShipmentTransitionService)shipments).InitiateRtoCalls, ndr.ShipmentId);
        var returnShipment = await dbContext.Set<ReturnShipment>().SingleAsync(r => r.OriginalShipmentId == ndr.ShipmentId);
        Assert.Equal(destinationBranchId, returnShipment.OriginBranchId);
        Assert.Equal(originBranchId, returnShipment.DestinationBranchId);
        Assert.StartsWith("RTO-", returnShipment.ReturnAwb);
        Assert.Equal("Customer refused after three contacts", returnShipment.RtoReason);
    }

    [Fact]
    public async Task ResolveAsync_RtoWithoutReason_ThrowsNdrValidationException()
    {
        await using var dbContext = CreateContext();
        var (ndr, service, _, _, _) = await SeedNdrAsync(dbContext, Guid.NewGuid(), NdrStatus.RtoRequested);

        await Assert.ThrowsAsync<NdrValidationException>(() => service.ResolveAsync(ndr.Id, new ResolveNdrRequest(NdrResolution.Rto, null), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task ResolveAsync_AlreadyResolved_ThrowsNdrValidationException()
    {
        await using var dbContext = CreateContext();
        var (ndr, service, _, _, _) = await SeedNdrAsync(dbContext, Guid.NewGuid(), NdrStatus.ReattemptScheduled);
        await service.ResolveAsync(ndr.Id, new ResolveNdrRequest(NdrResolution.Reattempt, null), ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<NdrValidationException>(() => service.ResolveAsync(ndr.Id, new ResolveNdrRequest(NdrResolution.Reattempt, null), ActingUser(Guid.NewGuid())));
    }
}
