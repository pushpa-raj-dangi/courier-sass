using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Events.Application;
using Courier.Identity.Application;
using Courier.Ndr.Application;
using Courier.Ndr.Domain;
using Courier.Riders.Application;
using Courier.Riders.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Tests;

// Shared by every RiderService construction in this project: RiderService needs both lookups,
// and only the R3b-2 reads call them. Unused reporting reads throw so an accidental call fails loudly.
internal sealed class StubShipmentReportingLookup : IShipmentReportingLookupService
{
    public Dictionary<Guid, string> Awbs { get; } = new();
    public int AwbLookupCalls { get; private set; }

    public Task<IReadOnlyDictionary<Guid, string>> GetAwbsByShipmentIdsAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default)
    {
        AwbLookupCalls++;
        IReadOnlyDictionary<Guid, string> result = shipmentIds.Where(Awbs.ContainsKey).ToDictionary(id => id, id => Awbs[id]);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCohortStatusCountsAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchShipmentCounts> GetBranchShipmentCountsAsync(Guid branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TimeSpan?> GetAverageDeliveryTimeAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetShipmentIdsByDestinationBranchAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ShipmentExportRow>> GetShipmentExportRowsAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCurrentStatusCountsAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<Guid, BranchCustodyCounts>> GetBranchCustodyCountsAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class StubUserDirectoryLookup : IUserDirectoryLookupService
{
    public Dictionary<Guid, string> Names { get; } = new();

    public Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<Guid, string> result = userIds.Distinct().Where(Names.ContainsKey).ToDictionary(id => id, id => Names[id]);
        return Task.FromResult(result);
    }
}

// InMemory context + RiderService factory for the R3b-2 read tests (board, today, scoped single
// read). The InMemory context has no tenant query filters; tenant isolation is pinned by Api.Tests.
internal static class RiderReadTestSupport
{
    public sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RiderConfiguration());
            modelBuilder.ApplyConfiguration(new VehicleConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryRunConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryTaskConfiguration());
            modelBuilder.ApplyConfiguration(new RiderLocationPingConfiguration());
        }
    }

    public static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    public static ClaimsPrincipal ActingUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    public static RiderService CreateService(
        DbContext dbContext, IReadOnlySet<Guid>? accessible,
        StubShipmentReportingLookup? shipments = null, StubUserDirectoryLookup? names = null) =>
        new(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), new ScopedBranchesChecker(accessible), new NotSupportedShipmentTransitionService(),
            new NotSupportedObjectStorage(), new NotSupportedNdrCreationService(), new NotSupportedCodCollectionService(),
            new AlwaysTruePermissionChecker(), new NoopOutboxWriter(), new NotSupportedShipmentTaskLookupService(),
            shipments ?? new StubShipmentReportingLookup(), names ?? new StubUserDirectoryLookup());

    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
        public Guid? OrganizationId { get; }
    }

    private sealed class NoopAuditLogger : IAuditLogger
    {
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ScopedBranchesChecker : IBranchAuthorizationChecker
    {
        private readonly IReadOnlySet<Guid>? _accessible;
        public ScopedBranchesChecker(IReadOnlySet<Guid>? accessible) => _accessible = accessible;
        public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(_accessible);
        public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_accessible is null || _accessible.Contains(branchId));
    }

    private sealed class NotSupportedUserProvisioningService : IUserProvisioningService
    {
        public Task<Guid> ProvisionStaffUserAsync(ProvisionStaffUserRequest request, Guid organizationId, Guid grantedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NotSupportedBranchLookup : IBranchLookupService
    {
        public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NotSupportedShipmentTransitionService : IShipmentTransitionService
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

    private sealed class NotSupportedObjectStorage : IObjectStorage
    {
        public Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NotSupportedNdrCreationService : INdrCreationService
    {
        public Task CreateFromFailedAttemptAsync(Guid shipmentId, Guid organizationId, NdrReason reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NotSupportedCodCollectionService : ICodCollectionService
    {
        public Task MarkCollectedAsync(Guid shipmentId, decimal collectedAmount, Guid riderId, Guid deliveryProofId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class AlwaysTruePermissionChecker : IPermissionChecker
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class NoopOutboxWriter : IOutboxWriter
    {
        public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NotSupportedShipmentTaskLookupService : IShipmentTaskLookupService
    {
        public Task<ShipmentTaskInfo?> GetTaskInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
