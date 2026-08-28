using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Courier.Merchants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Merchants.Tests;

public class MerchantLookupServiceReportingTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MerchantConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task GetDisplayNamesByIdsAsync_ReturnsDisplayNameForEachRequestedMerchant()
    {
        // Arrange: seed two Merchant rows via the InMemory context.
        await using var dbContext = CreateContext();
        var merchant1 = new Merchant
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            LegalName = "Acme Pvt. Ltd.",
            DisplayName = "Acme",
            ContactPhone = "01-4444444",
            ContactEmail = "acme@example.com",
            KycStatus = KycStatus.Pending,
            IsActive = true
        };
        var merchant2 = new Merchant
        {
            Id = Guid.NewGuid(),
            OrganizationId = merchant1.OrganizationId,
            LegalName = "Beta Corp Ltd.",
            DisplayName = "Beta Corp",
            ContactPhone = "01-5555555",
            ContactEmail = "beta@example.com",
            KycStatus = KycStatus.Approved,
            IsActive = true
        };
        dbContext.Set<Merchant>().Add(merchant1);
        dbContext.Set<Merchant>().Add(merchant2);
        await dbContext.SaveChangesAsync();

        var service = new MerchantLookupService(dbContext);

        // Act: call service.GetDisplayNamesByIdsAsync(new HashSet<Guid> { merchant1.Id, merchant2.Id });
        var result = await service.GetDisplayNamesByIdsAsync(new HashSet<Guid> { merchant1.Id, merchant2.Id });

        // Assert: dictionary has 2 entries, keyed by Id, valued by DisplayName.
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(merchant1.DisplayName, result[merchant1.Id]);
        Assert.Equal(merchant2.DisplayName, result[merchant2.Id]);
    }

    [Fact]
    public async Task GetDisplayNamesByIdsAsync_EmptySet_ReturnsEmptyDictionary()
    {
        // Arrange
        await using var dbContext = CreateContext();
        var service = new MerchantLookupService(dbContext);

        // Act: call with an empty HashSet<Guid>.
        var result = await service.GetDisplayNamesByIdsAsync(new HashSet<Guid>());

        // Assert: result.Count == 0, and no DB round trip is required for correctness (behavior, not
        // an implementation detail — just assert the empty result).
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
