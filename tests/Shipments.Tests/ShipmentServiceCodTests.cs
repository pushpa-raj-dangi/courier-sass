using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Events.Application;
using Courier.Geography.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Pricing.Infrastructure;
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

file class FakeBranchLookup : IBranchLookupService
{
    public Task<List<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    private readonly Dictionary<Guid, Guid?> _zonesByBranch;
    public FakeBranchLookup(Dictionary<Guid, Guid?> zonesByBranch) => _zonesByBranch = zonesByBranch;
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_zonesByBranch.TryGetValue(branchId, out var zoneId) ? new BranchZoneInfo(branchId, zoneId) : null);
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

file class AlwaysTrueGeographyLookup : IGeographyLookupService
{
    public Task<bool> IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid localLevelId, int wardNumber, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> DeliveryZoneExistsAsync(Guid zoneId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class FixedOrganizationLookup : IOrganizationLookupService
{
    public Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<List<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SuspendForNonPaymentAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    private readonly string? _slug;
    public FixedOrganizationLookup(string? slug) => _slug = slug;
    public Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(_slug);
    public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<PagedResult<OrganizationRecord>> ListAsync(string? search, bool? isActive, bool? isDeleted, int? page, int? pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<OrganizationRecord>(new List<OrganizationRecord>(), 1, 25, 0));
    public Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

// Not `file`-scoped (unlike the other fakes above): it's referenced as a named tuple-element type
// in ShipmentServiceCodTests.CreateService's return signature below, and C# forbids a file-local
// type from appearing in any member signature — including a private member's — of a type that is
// itself not file-local (CS9051). The other fakes above never appear in a signature (only as
// `new Fake(...)` arguments), so this restriction never applied to them.
class RecordingCodCollectionCreationService : ICodCollectionCreationService
{
    public List<(Guid ShipmentId, Guid MerchantId, Guid BranchId, decimal ExpectedAmount, decimal ShipmentTotalCharge)> Calls { get; } = new();

    public Task CreateExpectedAsync(Guid shipmentId, Guid organizationId, Guid merchantId, Guid branchId, decimal expectedAmount, decimal shipmentTotalCharge, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        Calls.Add((shipmentId, merchantId, branchId, expectedAmount, shipmentTotalCharge));
        return Task.CompletedTask;
    }
}

file class NoopOutboxWriter : IOutboxWriter
{
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class ShipmentServiceCodTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
            modelBuilder.ApplyConfiguration(new ServiceTypeConfiguration());
            modelBuilder.ApplyConfiguration(new WeightSlabConfiguration());
            modelBuilder.ApplyConfiguration(new RateCardConfiguration());
            modelBuilder.ApplyConfiguration(new ZoneRateConfiguration());
            modelBuilder.ApplyConfiguration(new AdditionalChargeConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantRateCardConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentItemConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentEventConfiguration());
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

    private static (ShipmentService Service, RecordingCodCollectionCreationService CodRecorder) CreateService(TestDbContext dbContext, Guid organizationId, Dictionary<Guid, Guid?> zonesByBranch)
    {
        var branchLookup = new FakeBranchLookup(zonesByBranch);
        var pricingService = new PricingService(dbContext, new FixedTenantProvider(organizationId), branchLookup, new AlwaysTrueMerchantLookup());
        var codRecorder = new RecordingCodCollectionCreationService();
        var service = new ShipmentService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), branchLookup,
            new AlwaysTrueGeographyLookup(), new AlwaysTrueMerchantLookup(), pricingService, new AllBranchesAuthorizationChecker(),
            new FixedOrganizationLookup("testorg"), codRecorder, new NoopOutboxWriter());
        return (service, codRecorder);
    }

    // Deviates from the brief's verbatim signature by also returning OriginZoneId/DestinationZoneId
    // (plan gap found during execution): the brief's version only returned branch/service-type ids,
    // but CreateService's FakeBranchLookup must be wired with the SAME zone ids used to seed the
    // ZoneRate below, or PricingService.QuoteAsync finds no matching rate — exactly the pattern
    // ShipmentServiceCreationTests.cs's SeedRoutableRateCard already establishes (it returns
    // OriginZoneId/DestinationZoneId for the same reason).
    private async Task<(Guid OriginBranchId, Guid DestinationBranchId, Guid OriginZoneId, Guid DestinationZoneId, Guid ServiceTypeId)> SeedRateCardAsync(TestDbContext dbContext, Guid organizationId)
    {
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;

        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Test Org", Slug = "testorg" });
        var rateCard = new RateCard { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Default", EffectiveFrom = DateTimeOffset.UtcNow, Version = 1, IsActive = true };
        dbContext.Set<RateCard>().Add(rateCard);
        dbContext.Set<ZoneRate>().Add(new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = weightSlabId, BaseRate = 100m });
        await dbContext.SaveChangesAsync();

        return (originBranchId, destinationBranchId, originZoneId, destinationZoneId, serviceTypeId);
    }

    private static CreateShipmentRequest ShipmentRequest(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, decimal codAmount) => new(
        merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, codAmount, false,
        "Recipient", "9800000000", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Recipient Address",
        "Sender", "9800000001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item", 1, 100m) });

    [Fact]
    public async Task CreateShipmentAsync_WithCodAmount_CallsCodCollectionCreationServiceWithDestinationBranch()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (originBranchId, destinationBranchId, originZoneId, destinationZoneId, serviceTypeId) = await SeedRateCardAsync(dbContext, organizationId);
        var (service, codRecorder) = CreateService(dbContext, organizationId, new() { [originBranchId] = originZoneId, [destinationBranchId] = destinationZoneId });
        var merchantId = Guid.NewGuid();

        var response = await service.CreateShipmentAsync(ShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId, 2500m), ActingUser(Guid.NewGuid()));

        var call = Assert.Single(codRecorder.Calls);
        Assert.Equal(response.Id, call.ShipmentId);
        Assert.Equal(merchantId, call.MerchantId);
        Assert.Equal(destinationBranchId, call.BranchId);
        Assert.Equal(2500m, call.ExpectedAmount);
        Assert.Equal(response.TotalCharge, call.ShipmentTotalCharge);
    }

    [Fact]
    public async Task CreateShipmentAsync_WithZeroCodAmount_NeverCallsCodCollectionCreationService()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (originBranchId, destinationBranchId, originZoneId, destinationZoneId, serviceTypeId) = await SeedRateCardAsync(dbContext, organizationId);
        var (service, codRecorder) = CreateService(dbContext, organizationId, new() { [originBranchId] = originZoneId, [destinationBranchId] = destinationZoneId });

        await service.CreateShipmentAsync(ShipmentRequest(Guid.NewGuid(), originBranchId, destinationBranchId, serviceTypeId, 0m), ActingUser(Guid.NewGuid()));

        Assert.Empty(codRecorder.Calls);
    }
}
