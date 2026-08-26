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
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file class NoopOutboxWriter : IOutboxWriter
{
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class CodServiceFinanceVerificationsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
            modelBuilder.ApplyConfiguration(new RiderCashSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new BranchCashSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new FinancialTransactionConfiguration());
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

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        new AllBranchesAuthorizationChecker(), new AlwaysTrueMerchantLookup(), new NoopOutboxWriter());

    private static async Task<BranchCashSettlement> SeedBranchReconciledAsync(TestDbContext dbContext, Guid organizationId, decimal amount)
    {
        var branchSettlement = new BranchCashSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, BranchId = Guid.NewGuid(),
            ConfirmedAmount = amount, ReconciledByUserId = Guid.NewGuid(), ReconciledAt = DateTimeOffset.UtcNow
        };
        dbContext.Set<BranchCashSettlement>().Add(branchSettlement);
        var riderSettlement = new RiderCashSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, RiderId = Guid.NewGuid(), BranchId = branchSettlement.BranchId,
            DeclaredAmount = amount, ShipmentCount = 1, SettledAt = DateTimeOffset.UtcNow, ConfirmedByUserId = Guid.NewGuid(),
            BranchCashSettlementId = branchSettlement.Id
        };
        dbContext.Set<RiderCashSettlement>().Add(riderSettlement);
        dbContext.Set<CODCollection>().Add(new CODCollection
        {
            Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = organizationId, MerchantId = Guid.NewGuid(),
            BranchId = branchSettlement.BranchId, ExpectedAmount = amount, ShipmentTotalCharge = 150m, CollectedAmount = amount,
            Status = CodCollectionStatus.BranchReconciled, RiderCashSettlementId = riderSettlement.Id
        });
        await dbContext.SaveChangesAsync();
        return branchSettlement;
    }

    [Fact]
    public async Task CreateFinanceVerificationAsync_WithBranchReconciled_BulkTransitionsToFinanceVerified()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchSettlement = await SeedBranchReconciledAsync(dbContext, organizationId, 2500m);
        var service = CreateService(dbContext, organizationId);

        var response = await service.CreateFinanceVerificationAsync(new CreateFinanceVerificationRequest(branchSettlement.Id, 2500m), ActingUser(Guid.NewGuid()));

        Assert.Equal(FinancialTransactionType.CodVerification, response.Type);
        var collection = await dbContext.Set<CODCollection>().SingleAsync();
        Assert.Equal(CodCollectionStatus.FinanceVerified, collection.Status);
    }

    [Fact]
    public async Task CreateFinanceVerificationAsync_SameBranchSettlementTwice_ThrowsOnSecondCall()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchSettlement = await SeedBranchReconciledAsync(dbContext, organizationId, 2500m);
        var service = CreateService(dbContext, organizationId);
        await service.CreateFinanceVerificationAsync(new CreateFinanceVerificationRequest(branchSettlement.Id, 2500m), ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.CreateFinanceVerificationAsync(new CreateFinanceVerificationRequest(branchSettlement.Id, 2500m), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateFinanceVerificationAsync_BranchCashSettlementBelongsToDifferentOrganization_ThrowsNotFound()
    {
        await using var dbContext = CreateContext();
        var otherOrganizationId = Guid.NewGuid();
        var branchSettlement = await SeedBranchReconciledAsync(dbContext, otherOrganizationId, 2500m);
        var callingOrganizationId = Guid.NewGuid();
        var service = CreateService(dbContext, callingOrganizationId);

        var exception = await Assert.ThrowsAsync<CodValidationException>(() =>
            service.CreateFinanceVerificationAsync(new CreateFinanceVerificationRequest(branchSettlement.Id, 2500m), ActingUser(Guid.NewGuid())));

        Assert.Equal("BranchCashSettlementId was not found.", exception.Message);
        var collection = await dbContext.Set<CODCollection>().SingleAsync();
        Assert.Equal(CodCollectionStatus.BranchReconciled, collection.Status);
    }
}
