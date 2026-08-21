using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Ndr.Application;
using Courier.Ndr.Domain;
using Courier.Ndr.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Ndr.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public List<AuditEntry> Entries { get; } = new();
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

// Not file-scoped: only one file in this project needs it, but keeping the naming convention
// consistent with the other *FakeShipmentTransitionService classes across the solution.
class RecordingShipmentTransitionService : IShipmentTransitionService
{
    public List<Guid> NdrTransitionCalls { get; } = new();

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        NdrTransitionCalls.Add(shipmentId);
        return Task.FromResult<ShipmentResponse?>(null);
    }
    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class NdrCreationServiceTests
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

    [Fact]
    public async Task CreateFromFailedAttemptAsync_TransitionsShipmentThenCreatesOpenNdr()
    {
        await using var dbContext = CreateContext();
        var shipmentTransitions = new RecordingShipmentTransitionService();
        var auditLogger = new NoopAuditLogger();
        var service = new NdrCreationService(dbContext, auditLogger, shipmentTransitions);

        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await service.CreateFromFailedAttemptAsync(shipmentId, organizationId, NdrReason.CustomerUnavailable, ActingUser(userId));

        Assert.Single(shipmentTransitions.NdrTransitionCalls);
        Assert.Equal(shipmentId, shipmentTransitions.NdrTransitionCalls[0]);

        var ndr = await dbContext.Set<NDR>().SingleAsync(n => n.ShipmentId == shipmentId);
        Assert.Equal(organizationId, ndr.OrganizationId);
        Assert.Equal(NdrReason.CustomerUnavailable, ndr.Reason);
        Assert.Equal(NdrStatus.Open, ndr.Status);
        Assert.Null(ndr.AssignedStaffUserId);
        Assert.Null(ndr.ResolvedAt);

        Assert.Single(auditLogger.Entries);
        Assert.Equal("ndr.created", auditLogger.Entries[0].Action);
        Assert.Equal(organizationId, auditLogger.Entries[0].OrganizationId);
        Assert.Equal(userId, auditLogger.Entries[0].UserId);
    }
}
