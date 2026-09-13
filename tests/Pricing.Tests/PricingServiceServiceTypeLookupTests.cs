using Courier.Branches.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Pricing.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Pricing.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NullBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult<BranchZoneInfo?>(null);
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

public class PricingServiceServiceTypeLookupTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ServiceTypeConfiguration());
            modelBuilder.ApplyConfiguration(new WeightSlabConfiguration());
            modelBuilder.ApplyConfiguration(new RateCardConfiguration());
            modelBuilder.ApplyConfiguration(new ZoneRateConfiguration());
            modelBuilder.ApplyConfiguration(new AdditionalChargeConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantRateCardConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task ExistsAsync_KnownSeededServiceType_ReturnsTrue()
    {
        await using var dbContext = CreateContext();
        var standardId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        IServiceTypeLookupService service = new PricingService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NullBranchLookup(), new AlwaysTrueMerchantLookup());

        Assert.True(await service.ExistsAsync(standardId));
    }

    [Fact]
    public async Task ExistsAsync_UnknownServiceType_ReturnsFalse()
    {
        await using var dbContext = CreateContext();
        IServiceTypeLookupService service = new PricingService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NullBranchLookup(), new AlwaysTrueMerchantLookup());

        Assert.False(await service.ExistsAsync(Guid.NewGuid()));
    }
}
