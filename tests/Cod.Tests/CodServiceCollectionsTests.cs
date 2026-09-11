using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
using Courier.Events.Application;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Cod.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class RecordingOutboxWriter : IOutboxWriter
{
    public List<(Guid OrganizationId, string EventType, object Payload)> Writes { get; } = new();
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default)
    {
        Writes.Add((organizationId, eventType, payload));
        return Task.CompletedTask;
    }
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class AlwaysTrueBranchLookup : IBranchLookupService
{
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class AlwaysTrueShipmentAwbLookup : IShipmentAwbLookupService
{
    public Task<IReadOnlyDictionary<Guid, string>> GetAwbsByShipmentIdsAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

public class CodServiceCollectionsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
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

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId, IOutboxWriter? outboxWriter = null) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        new AllBranchesAuthorizationChecker(), new AlwaysTrueMerchantLookup(), new AlwaysTrueShipmentAwbLookup(), outboxWriter ?? new RecordingOutboxWriter());

    [Fact]
    public async Task CreateExpectedAsync_PersistsRowWithCodExpectedStatus()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId);
        var shipmentId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        await service.CreateExpectedAsync(shipmentId, organizationId, merchantId, branchId, 2500m, 150m, ActingUser(Guid.NewGuid()));

        var collection = await dbContext.Set<CODCollection>().SingleAsync(c => c.ShipmentId == shipmentId);
        Assert.Equal(CodCollectionStatus.CodExpected, collection.Status);
        Assert.Equal(2500m, collection.ExpectedAmount);
        Assert.Equal(150m, collection.ShipmentTotalCharge);
        Assert.Equal(merchantId, collection.MerchantId);
        Assert.Equal(branchId, collection.BranchId);
        Assert.Null(collection.CollectedAmount);
    }

    [Fact]
    public async Task MarkCollectedAsync_FromCodExpected_TransitionsToCodCollected()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId);
        var shipmentId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        var deliveryProofId = Guid.NewGuid();
        await service.CreateExpectedAsync(shipmentId, organizationId, Guid.NewGuid(), Guid.NewGuid(), 2500m, 150m, ActingUser(Guid.NewGuid()));

        await service.MarkCollectedAsync(shipmentId, 2500m, riderId, deliveryProofId, ActingUser(Guid.NewGuid()));

        var collection = await dbContext.Set<CODCollection>().SingleAsync(c => c.ShipmentId == shipmentId);
        Assert.Equal(CodCollectionStatus.CodCollected, collection.Status);
        Assert.Equal(2500m, collection.CollectedAmount);
        Assert.Equal(riderId, collection.CollectedByRiderId);
        Assert.Equal(deliveryProofId, collection.DeliveryProofId);
        Assert.NotNull(collection.CollectedAt);
    }

    [Fact]
    public async Task MarkCollectedAsync_WritesCodCollectedOutboxMessage()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var recordingWriter = new RecordingOutboxWriter();
        var service = CreateService(dbContext, organizationId, recordingWriter);
        var shipmentId = Guid.NewGuid();
        await service.CreateExpectedAsync(shipmentId, organizationId, merchantId, Guid.NewGuid(), 2500m, 150m, ActingUser(Guid.NewGuid()));

        await service.MarkCollectedAsync(shipmentId, 2500m, Guid.NewGuid(), Guid.NewGuid(), ActingUser(Guid.NewGuid()));

        Assert.Single(recordingWriter.Writes, w => w.EventType == "cod.collected" && w.OrganizationId == organizationId);
    }

    [Fact]
    public async Task MarkCollectedAsync_NoCodCollectionForShipment_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.MarkCollectedAsync(Guid.NewGuid(), 100m, Guid.NewGuid(), Guid.NewGuid(), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task MarkCollectedAsync_AlreadyCollected_ThrowsAndDoesNotOverwrite()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId);
        var shipmentId = Guid.NewGuid();
        await service.CreateExpectedAsync(shipmentId, organizationId, Guid.NewGuid(), Guid.NewGuid(), 2500m, 150m, ActingUser(Guid.NewGuid()));
        await service.MarkCollectedAsync(shipmentId, 2500m, Guid.NewGuid(), Guid.NewGuid(), ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.MarkCollectedAsync(shipmentId, 999m, Guid.NewGuid(), Guid.NewGuid(), ActingUser(Guid.NewGuid())));

        var collection = await dbContext.Set<CODCollection>().SingleAsync(c => c.ShipmentId == shipmentId);
        Assert.Equal(2500m, collection.CollectedAmount);
    }
}
