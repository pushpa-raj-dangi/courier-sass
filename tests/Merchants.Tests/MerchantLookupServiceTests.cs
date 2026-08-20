using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Courier.Merchants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Merchants.Tests;

public class MerchantLookupServiceTests
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
    public async Task ExistsAsync_KnownMerchant_ReturnsTrue()
    {
        await using var dbContext = CreateContext();
        var merchant = new Merchant
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
        dbContext.Set<Merchant>().Add(merchant);
        await dbContext.SaveChangesAsync();

        var service = new MerchantLookupService(dbContext);

        Assert.True(await service.ExistsAsync(merchant.Id));
    }

    [Fact]
    public async Task ExistsAsync_UnknownMerchant_ReturnsFalse()
    {
        await using var dbContext = CreateContext();
        var service = new MerchantLookupService(dbContext);

        Assert.False(await service.ExistsAsync(Guid.NewGuid()));
    }
}
