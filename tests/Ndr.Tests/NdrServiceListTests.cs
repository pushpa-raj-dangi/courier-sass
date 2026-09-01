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

// Non-null GetAccessibleBranchIdsAsync result — the "caller is restricted to a specific set of
// branches" fake used to prove ListAsync/ListReturnShipmentsAsync actually filter (Phase 9's final
// review found zero coverage of this path).
file class RestrictedBranchAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid> _accessibleBranchIds;
    public RestrictedBranchAuthorizationChecker(params Guid[] accessibleBranchIds) => _accessibleBranchIds = accessibleBranchIds.ToHashSet();
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(_accessibleBranchIds);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessibleBranchIds.Contains(branchId));
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

public class NdrServiceListTests
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
    public async Task ListAsync_BranchRestricted_FiltersToAccessibleBranchOnly()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var accessibleBranchId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();
        var accessibleShipmentId = Guid.NewGuid();
        var otherShipmentId = Guid.NewGuid();

        var accessibleNdr = new NDR { Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = accessibleShipmentId, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open };
        var otherNdr = new NDR { Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = otherShipmentId, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open };
        dbContext.Set<NDR>().AddRange(accessibleNdr, otherNdr);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        shipments.ShipmentsById[accessibleShipmentId] = new ShipmentScanInfo(accessibleShipmentId, organizationId, "AWB001", ShipmentStatus.NdrCreated, null, Guid.NewGuid(), accessibleBranchId, null);
        shipments.ShipmentsById[otherShipmentId] = new ShipmentScanInfo(otherShipmentId, organizationId, "AWB002", ShipmentStatus.NdrCreated, null, Guid.NewGuid(), otherBranchId, null);

        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new RestrictedBranchAuthorizationChecker(accessibleBranchId));

        var result = await service.ListAsync(null, null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(accessibleNdr.Id, single.Id);
    }

    [Fact]
    public async Task ListReturnShipmentsAsync_BranchRestricted_FiltersToAccessibleBranchOnly()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var accessibleBranchId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();
        var accessibleOriginalShipmentId = Guid.NewGuid();
        var otherOriginalShipmentId = Guid.NewGuid();

        var accessibleReturnShipment = new ReturnShipment
        {
            Id = Guid.NewGuid(), OriginalShipmentId = accessibleOriginalShipmentId, OrganizationId = organizationId,
            ReturnAwb = "RTO-20260821-001", RtoReason = "Customer refused",
            OriginBranchId = Guid.NewGuid(), DestinationBranchId = accessibleBranchId
        };
        var otherReturnShipment = new ReturnShipment
        {
            Id = Guid.NewGuid(), OriginalShipmentId = otherOriginalShipmentId, OrganizationId = organizationId,
            ReturnAwb = "RTO-20260821-002", RtoReason = "Wrong address",
            OriginBranchId = Guid.NewGuid(), DestinationBranchId = otherBranchId
        };
        dbContext.Set<ReturnShipment>().AddRange(accessibleReturnShipment, otherReturnShipment);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        shipments.ShipmentsById[accessibleOriginalShipmentId] = new ShipmentScanInfo(accessibleOriginalShipmentId, organizationId, "AWB001", ShipmentStatus.RtoAtOrigin, accessibleBranchId, accessibleBranchId, Guid.NewGuid(), null);
        shipments.ShipmentsById[otherOriginalShipmentId] = new ShipmentScanInfo(otherOriginalShipmentId, organizationId, "AWB002", ShipmentStatus.RtoAtOrigin, otherBranchId, otherBranchId, Guid.NewGuid(), null);

        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new RestrictedBranchAuthorizationChecker(accessibleBranchId));

        var result = await service.ListReturnShipmentsAsync(null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result);
        Assert.Equal(accessibleReturnShipment.Id, single.Id);
    }

    [Fact]
    public async Task ListAsync_AllBranches_IncludesShipmentAwb()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();

        var ndr = new NDR { Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = shipmentId, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open };
        dbContext.Set<NDR>().Add(ndr);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        shipments.ShipmentsById[shipmentId] = new ShipmentScanInfo(shipmentId, organizationId, "AWB999", ShipmentStatus.NdrCreated, null, Guid.NewGuid(), Guid.NewGuid(), null);

        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new AllBranchesAuthorizationChecker());

        var result = await service.ListAsync(null, null, null, null, ActingUser(Guid.NewGuid()));

        Assert.Equal("AWB999", Assert.Single(result.Items).Awb);
    }

    [Fact]
    public async Task ListAsync_FilteredByShipmentId_ReturnsOnlyMatchingNdrs()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var targetShipmentId = Guid.NewGuid();
        var otherShipmentId = Guid.NewGuid();

        var targetNdr = new NDR { Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = targetShipmentId, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open };
        var otherNdr = new NDR { Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = otherShipmentId, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open };
        dbContext.Set<NDR>().AddRange(targetNdr, otherNdr);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        shipments.ShipmentsById[targetShipmentId] = new ShipmentScanInfo(targetShipmentId, organizationId, "AWB001", ShipmentStatus.NdrCreated, null, Guid.NewGuid(), Guid.NewGuid(), null);
        shipments.ShipmentsById[otherShipmentId] = new ShipmentScanInfo(otherShipmentId, organizationId, "AWB002", ShipmentStatus.NdrCreated, null, Guid.NewGuid(), Guid.NewGuid(), null);

        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new AllBranchesAuthorizationChecker());

        var result = await service.ListAsync(null, null, null, targetShipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(targetNdr.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task ListReturnShipmentsAsync_FilteredByOriginalShipmentId_ReturnsOnlyMatching()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var targetOriginalShipmentId = Guid.NewGuid();
        var otherOriginalShipmentId = Guid.NewGuid();

        var targetReturnShipment = new ReturnShipment
        {
            Id = Guid.NewGuid(), OriginalShipmentId = targetOriginalShipmentId, OrganizationId = organizationId,
            ReturnAwb = "RTO-20260901-001", RtoReason = "Customer refused",
            OriginBranchId = Guid.NewGuid(), DestinationBranchId = Guid.NewGuid()
        };
        var otherReturnShipment = new ReturnShipment
        {
            Id = Guid.NewGuid(), OriginalShipmentId = otherOriginalShipmentId, OrganizationId = organizationId,
            ReturnAwb = "RTO-20260901-002", RtoReason = "Wrong address",
            OriginBranchId = Guid.NewGuid(), DestinationBranchId = Guid.NewGuid()
        };
        dbContext.Set<ReturnShipment>().AddRange(targetReturnShipment, otherReturnShipment);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        shipments.ShipmentsById[targetOriginalShipmentId] = new ShipmentScanInfo(targetOriginalShipmentId, organizationId, "AWB001", ShipmentStatus.RtoAtOrigin, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);
        shipments.ShipmentsById[otherOriginalShipmentId] = new ShipmentScanInfo(otherOriginalShipmentId, organizationId, "AWB002", ShipmentStatus.RtoAtOrigin, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);

        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new AllBranchesAuthorizationChecker());

        var result = await service.ListReturnShipmentsAsync(targetOriginalShipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(targetReturnShipment.Id, Assert.Single(result).Id);
    }

    [Fact]
    public async Task GetByIdAsync_IncludesShipmentAwb()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();

        var ndr = new NDR { Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = shipmentId, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open };
        dbContext.Set<NDR>().Add(ndr);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        shipments.ShipmentsById[shipmentId] = new ShipmentScanInfo(shipmentId, organizationId, "AWB998", ShipmentStatus.NdrCreated, null, Guid.NewGuid(), Guid.NewGuid(), null);

        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new AllBranchesAuthorizationChecker());

        var result = await service.GetByIdAsync(ndr.Id);

        Assert.Equal("AWB998", result!.Awb);
    }
}
