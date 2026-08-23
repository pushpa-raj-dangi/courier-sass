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

file class FakeShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();
    public List<Guid> ConfirmCalls { get; } = new();

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
    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        ConfirmCalls.Add(shipmentId);
        if (ShipmentsById.TryGetValue(shipmentId, out var info))
        {
            ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.RtoDelivered };
        }
        return Task.FromResult<ShipmentResponse?>(null);
    }
}

public class NdrServiceReturnShipmentTests
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

    // Shipments is typed as the IShipmentTransitionService interface, not the file-local
    // FakeShipmentTransitionService, because CS9051 forbids exposing a file-local type through a
    // member signature of this non-file-local test class (same fix as
    // NdrServiceResolveTests.SeedNdrAsync). Tests that need the recorded call list downcast the
    // returned reference back to FakeShipmentTransitionService in their own body.
    private static async Task<(ReturnShipment Entity, NdrService Service, IShipmentTransitionService Shipments)> SeedReturnShipmentAsync(TestDbContext dbContext, Guid organizationId, ShipmentStatus originalShipmentStatus)
    {
        var originalShipmentId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var returnShipment = new ReturnShipment
        {
            Id = Guid.NewGuid(), OriginalShipmentId = originalShipmentId, OrganizationId = organizationId,
            ReturnAwb = "RTO-20260821-001", RtoReason = "Customer refused",
            OriginBranchId = destinationBranchId, DestinationBranchId = originBranchId
        };
        dbContext.Set<ReturnShipment>().Add(returnShipment);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        shipments.ShipmentsById[originalShipmentId] = new ShipmentScanInfo(originalShipmentId, organizationId, "AWB001", originalShipmentStatus, originBranchId, originBranchId, destinationBranchId, null);
        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new AllBranchesAuthorizationChecker());
        return (returnShipment, service, shipments);
    }

    [Fact]
    public async Task GetReturnShipmentByIdAsync_ReflectsLiveShipmentStatus()
    {
        await using var dbContext = CreateContext();
        var (returnShipment, service, _) = await SeedReturnShipmentAsync(dbContext, Guid.NewGuid(), ShipmentStatus.RtoAtOrigin);

        var result = await service.GetReturnShipmentByIdAsync(returnShipment.Id);

        Assert.Equal(ShipmentStatus.RtoAtOrigin, result!.CurrentStatus);
        Assert.Equal(returnShipment.ReturnAwb, result.ReturnAwb);
    }

    [Fact]
    public async Task ConfirmReturnShipmentAsync_FromRtoAtOrigin_SetsMerchantConfirmedAtAndTransitionsShipment()
    {
        await using var dbContext = CreateContext();
        var (returnShipment, service, shipments) = await SeedReturnShipmentAsync(dbContext, Guid.NewGuid(), ShipmentStatus.RtoAtOrigin);

        var result = await service.ConfirmReturnShipmentAsync(returnShipment.Id, ActingUser(Guid.NewGuid()));

        Assert.NotNull(result!.MerchantConfirmedAt);
        Assert.Single(((FakeShipmentTransitionService)shipments).ConfirmCalls, returnShipment.OriginalShipmentId);
        Assert.Equal(ShipmentStatus.RtoDelivered, result.CurrentStatus);
    }

    [Fact]
    public async Task ConfirmReturnShipmentAsync_AlreadyConfirmed_ThrowsNdrValidationException()
    {
        await using var dbContext = CreateContext();
        var (returnShipment, service, _) = await SeedReturnShipmentAsync(dbContext, Guid.NewGuid(), ShipmentStatus.RtoAtOrigin);
        await service.ConfirmReturnShipmentAsync(returnShipment.Id, ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<NdrValidationException>(() => service.ConfirmReturnShipmentAsync(returnShipment.Id, ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task GetReturnShipmentByIdAsync_UnknownId_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = new NdrService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new FakeShipmentTransitionService(), new AllBranchesAuthorizationChecker());

        Assert.Null(await service.GetReturnShipmentByIdAsync(Guid.NewGuid()));
    }
}
