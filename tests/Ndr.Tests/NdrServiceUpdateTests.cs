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
    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class NdrServiceUpdateTests
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

    private static ShipmentScanInfo Info(Guid shipmentId, Guid originBranchId, Guid destinationBranchId, ShipmentStatus status = ShipmentStatus.NdrCreated) => new(
        shipmentId, Guid.NewGuid(), "AWB001", status, null, originBranchId, destinationBranchId, null);

    // Returns only (Entity, Service) — not the FakeShipmentTransitionService itself: since that type
    // is file-local (needed to avoid colliding with NdrCreationServiceTests.cs's own
    // RecordingShipmentTransitionService in the same namespace), CS9051 forbids exposing it through
    // a member signature of this non-file-local test class. None of this file's tests need to
    // inspect the fake's recorded calls, so dropping it from the tuple is lossless.
    private static async Task<(NDR Entity, NdrService Service)> SeedOpenNdrAsync(TestDbContext dbContext, Guid organizationId, ShipmentScanInfo info)
    {
        var ndr = new NDR { Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = info.ShipmentId, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open };
        dbContext.Set<NDR>().Add(ndr);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        shipments.ShipmentsById[info.ShipmentId] = info;
        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new AllBranchesAuthorizationChecker());
        return (ndr, service);
    }

    [Fact]
    public async Task UpdateAsync_OpenToContacting_Succeeds()
    {
        await using var dbContext = CreateContext();
        var info = Info(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (ndr, service) = await SeedOpenNdrAsync(dbContext, Guid.NewGuid(), info);

        var result = await service.UpdateAsync(ndr.Id, new UpdateNdrRequest(NdrStatus.Contacting, "No answer", null, null), ActingUser(Guid.NewGuid()));

        Assert.Equal(NdrStatus.Contacting, result!.Status);
        Assert.Equal("No answer", result.CustomerResponse);
    }

    [Fact]
    public async Task UpdateAsync_SkippedMove_ThrowsNdrValidationException()
    {
        await using var dbContext = CreateContext();
        var info = Info(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (ndr, service) = await SeedOpenNdrAsync(dbContext, Guid.NewGuid(), info);

        await Assert.ThrowsAsync<NdrValidationException>(() => service.UpdateAsync(ndr.Id, new UpdateNdrRequest(NdrStatus.CustomerConfirmed, null, null, null), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task UpdateAsync_BackwardMove_ThrowsNdrValidationException()
    {
        await using var dbContext = CreateContext();
        var info = Info(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (ndr, service) = await SeedOpenNdrAsync(dbContext, Guid.NewGuid(), info);
        await service.UpdateAsync(ndr.Id, new UpdateNdrRequest(NdrStatus.Contacting, null, null, null), ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<NdrValidationException>(() => service.UpdateAsync(ndr.Id, new UpdateNdrRequest(NdrStatus.Open, null, null, null), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = new NdrService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new FakeShipmentTransitionService(), new AllBranchesAuthorizationChecker());

        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateNdrRequest(NdrStatus.Contacting, null, null, null), ActingUser(Guid.NewGuid()));

        Assert.Null(result);
    }
}
