using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.Shipments.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Shipments.Tests;

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

file class RestrictedBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid> _accessibleBranchIds;
    public RestrictedBranchesAuthorizationChecker(IReadOnlySet<Guid> accessibleBranchIds) => _accessibleBranchIds = accessibleBranchIds;
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(_accessibleBranchIds);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessibleBranchIds.Contains(branchId));
}

public class PickupRequestServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ShipmentConfiguration());
            modelBuilder.ApplyConfiguration(new PickupRequestConfiguration());
        }
    }

    private static TestDbContext CreateContext() => new(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static ClaimsPrincipal StaffUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));
    private static ClaimsPrincipal MerchantUser(Guid merchantId) => new(new ClaimsIdentity(new[] { new Claim("merchant_id", merchantId.ToString()) }, "test"));

    // Inserted directly (bypassing ShipmentService) — this test only needs existing Shipment
    // rows with a known MerchantId/OriginBranchId, not full booking/pricing validation.
    private static Shipment NewShipment(Guid merchantId, Guid originBranchId) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = Guid.NewGuid(),
        Awb = $"AWB-{Guid.NewGuid():N}"[..12],
        MerchantId = merchantId,
        OriginBranchId = originBranchId,
        DestinationBranchId = Guid.NewGuid(),
        CurrentStatus = ShipmentStatus.Created,
        ServiceTypeId = Guid.NewGuid(),
        WeightKg = 1m,
        RecipientName = "Recipient", RecipientPhone = "9800000000",
        RecipientProvinceId = Guid.NewGuid(), RecipientDistrictId = Guid.NewGuid(), RecipientLocalLevelId = Guid.NewGuid(), RecipientWardNumber = 1, RecipientAddressLine = "Addr",
        SenderName = "Sender", SenderPhone = "9800000001",
        SenderProvinceId = Guid.NewGuid(), SenderDistrictId = Guid.NewGuid(), SenderLocalLevelId = Guid.NewGuid(), SenderWardNumber = 1, SenderAddressLine = "Addr"
    };

    private static PickupRequestService CreateService(TestDbContext dbContext, Guid organizationId, IBranchAuthorizationChecker? branchAuthorizationChecker = null) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), branchAuthorizationChecker ?? new AllBranchesAuthorizationChecker());

    [Fact]
    public async Task CreatePickupRequestAsync_ShipmentsShareOriginBranch_CreatesRequestForThatBranch()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var shipmentA = NewShipment(merchantId, originBranchId);
        var shipmentB = NewShipment(merchantId, originBranchId);
        dbContext.Set<Shipment>().AddRange(shipmentA, shipmentB);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId);

        var result = await service.CreatePickupRequestAsync(
            new CreatePickupRequestRequest(new[] { shipmentA.Id, shipmentB.Id }, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), "Ring the bell"),
            MerchantUser(merchantId));

        Assert.Equal(originBranchId, result.BranchId);
        Assert.Equal(PickupRequestStatus.Requested, result.Status);
    }

    [Fact]
    public async Task CreatePickupRequestAsync_ShipmentBelongsToAnotherMerchant_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var foreignShipment = NewShipment(otherMerchantId, originBranchId);
        dbContext.Set<Shipment>().Add(foreignShipment);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<ShipmentValidationException>(() =>
            service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { foreignShipment.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(merchantId)));
    }

    [Fact]
    public async Task CreatePickupRequestAsync_ShipmentsWithDifferentOriginBranches_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var shipmentA = NewShipment(merchantId, Guid.NewGuid());
        var shipmentB = NewShipment(merchantId, Guid.NewGuid());
        dbContext.Set<Shipment>().AddRange(shipmentA, shipmentB);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<ShipmentValidationException>(() =>
            service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { shipmentA.Id, shipmentB.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(merchantId)));
    }

    [Fact]
    public async Task ListAsync_MerchantCaller_OnlySeesOwnRequests()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var mine = NewShipment(merchantId, branchId);
        var theirs = NewShipment(otherMerchantId, branchId);
        dbContext.Set<Shipment>().AddRange(mine, theirs);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId);
        await service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { mine.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(merchantId));
        await service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { theirs.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(otherMerchantId));

        var result = await service.ListAsync(MerchantUser(merchantId));

        Assert.All(result, p => Assert.Equal(merchantId, p.MerchantId));
    }

    [Fact]
    public async Task ListAsync_StaffCallerRestrictedToOneBranch_OnlySeesRequestsForThatBranch()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var inScopeBranch = Guid.NewGuid();
        var outOfScopeBranch = Guid.NewGuid();
        var inScopeShipment = NewShipment(Guid.NewGuid(), inScopeBranch);
        var outOfScopeShipment = NewShipment(Guid.NewGuid(), outOfScopeBranch);
        dbContext.Set<Shipment>().AddRange(inScopeShipment, outOfScopeShipment);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId);
        var inScopeRequest = await service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { inScopeShipment.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(inScopeShipment.MerchantId));
        await service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { outOfScopeShipment.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(outOfScopeShipment.MerchantId));
        var restrictedService = CreateService(dbContext, organizationId, new RestrictedBranchesAuthorizationChecker(new HashSet<Guid> { inScopeBranch }));

        var result = await restrictedService.ListAsync(StaffUser(Guid.NewGuid()));

        var resultIds = result.Select(p => p.Id).ToHashSet();
        Assert.Contains(inScopeRequest.Id, resultIds);
        Assert.Single(result);
    }

    [Fact]
    public async Task CancelAsync_OwnRequest_SetsCancelledStatus()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var shipment = NewShipment(merchantId, Guid.NewGuid());
        dbContext.Set<Shipment>().Add(shipment);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, Guid.NewGuid());
        var created = await service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { shipment.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(merchantId));

        var result = await service.CancelAsync(created.Id, MerchantUser(merchantId));

        Assert.NotNull(result);
        Assert.Equal(PickupRequestStatus.Cancelled, result!.Status);
    }

    [Fact]
    public async Task CancelAsync_AlreadyCancelled_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var shipment = NewShipment(merchantId, Guid.NewGuid());
        dbContext.Set<Shipment>().Add(shipment);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, Guid.NewGuid());
        var created = await service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { shipment.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(merchantId));
        await service.CancelAsync(created.Id, MerchantUser(merchantId));

        await Assert.ThrowsAsync<ShipmentValidationException>(() => service.CancelAsync(created.Id, MerchantUser(merchantId)));
    }

    [Fact]
    public async Task CancelAsync_BelongsToAnotherMerchant_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        var shipment = NewShipment(merchantId, Guid.NewGuid());
        dbContext.Set<Shipment>().Add(shipment);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, Guid.NewGuid());
        var created = await service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { shipment.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(merchantId));

        var result = await service.CancelAsync(created.Id, MerchantUser(otherMerchantId));

        Assert.Null(result);
    }
}
