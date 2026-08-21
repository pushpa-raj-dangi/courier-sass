using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
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

file class FakeBranchLookup : IBranchLookupService
{
    private readonly HashSet<Guid> _knownBranchIds;
    public FakeBranchLookup(params Guid[] knownBranchIds) => _knownBranchIds = knownBranchIds.ToHashSet();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_knownBranchIds.Contains(branchId) ? new BranchZoneInfo(branchId, null) : null);
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

// Not file-scoped (unlike the fakes above): returned from CreateService's tuple, and a file-local
// type cannot appear in a member signature of RiderServiceCreationTests (a non-file-local type) —
// same reasoning as Manifests.Tests' ScanningFakeShipmentTransitionService (Phase 7).
class FakeUserProvisioningService : IUserProvisioningService
{
    public List<ProvisionStaffUserRequest> Calls { get; } = new();
    public bool ThrowOnNextCall { get; set; }

    public Task<Guid> ProvisionStaffUserAsync(ProvisionStaffUserRequest request, Guid organizationId, Guid grantedByUserId, CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextCall)
        {
            throw new UserProvisioningException("Email is already registered.");
        }
        Calls.Add(request);
        return Task.FromResult(Guid.NewGuid());
    }
}

file class NotSupportedShipmentTransitionService : IShipmentTransitionService
{
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
    public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class NullObjectStorage : IObjectStorage
{
    public Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class NotSupportedNdrCreationService : INdrCreationService
{
    public Task CreateFromFailedAttemptAsync(Guid shipmentId, Guid organizationId, NdrReason reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class RiderServiceCreationTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RiderConfiguration());
            modelBuilder.ApplyConfiguration(new VehicleConfiguration());
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

    private static (RiderService Service, FakeUserProvisioningService Provisioning) CreateService(TestDbContext dbContext, Guid organizationId, Guid knownBranchId)
    {
        var provisioning = new FakeUserProvisioningService();
        var service = new RiderService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), provisioning,
            new FakeBranchLookup(knownBranchId), new AllBranchesAuthorizationChecker(),
            new NotSupportedShipmentTransitionService(), new NullObjectStorage(), new NotSupportedNdrCreationService());
        return (service, provisioning);
    }

    [Fact]
    public async Task CreateRiderAsync_ValidRequest_ProvisionsUserAndCreatesRider()
    {
        await using var dbContext = CreateContext();
        var branchId = Guid.NewGuid();
        var (service, provisioning) = CreateService(dbContext, Guid.NewGuid(), branchId);

        var result = await service.CreateRiderAsync(
            new CreateRiderRequest("rider@example.com", "P@ssw0rd123!", "Test Rider", null, "LIC-001", branchId, RiderRoleType.DeliveryRider),
            ActingUser(Guid.NewGuid()));

        Assert.Equal(branchId, result.BaseBranchId);
        Assert.Equal(RiderRoleType.DeliveryRider, result.RoleType);
        Assert.True(result.IsActive);
        var call = Assert.Single(provisioning.Calls);
        Assert.Equal("DeliveryRider", call.RoleName);
        Assert.Equal(branchId, call.BranchId);
    }

    [Fact]
    public async Task CreateRiderAsync_UnknownBranch_ThrowsRiderValidationException()
    {
        await using var dbContext = CreateContext();
        var (service, _) = CreateService(dbContext, Guid.NewGuid(), Guid.NewGuid());

        await Assert.ThrowsAsync<RiderValidationException>(() => service.CreateRiderAsync(
            new CreateRiderRequest("rider@example.com", "P@ssw0rd123!", "Test Rider", null, "LIC-001", Guid.NewGuid(), RiderRoleType.PickupRider),
            ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateRiderAsync_DuplicateEmail_ThrowsRiderValidationException()
    {
        await using var dbContext = CreateContext();
        var branchId = Guid.NewGuid();
        var (service, provisioning) = CreateService(dbContext, Guid.NewGuid(), branchId);
        provisioning.ThrowOnNextCall = true;

        await Assert.ThrowsAsync<RiderValidationException>(() => service.CreateRiderAsync(
            new CreateRiderRequest("dup@example.com", "P@ssw0rd123!", "Test Rider", null, "LIC-001", branchId, RiderRoleType.PickupRider),
            ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateVehicleAsync_WithoutRider_Succeeds()
    {
        await using var dbContext = CreateContext();
        var (service, _) = CreateService(dbContext, Guid.NewGuid(), Guid.NewGuid());

        var result = await service.CreateVehicleAsync(
            new CreateVehicleRequest(null, "Motorcycle", "BA 2 KHA 1234", 50m), ActingUser(Guid.NewGuid()));

        Assert.Null(result.RiderId);
        Assert.Equal("BA 2 KHA 1234", result.PlateNumber);
    }
}
