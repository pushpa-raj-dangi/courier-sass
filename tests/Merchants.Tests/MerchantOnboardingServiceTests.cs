using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Geography.Application;
using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Courier.Merchants.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Merchants.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

// A stub, not the real Geography module — MerchantOnboardingServiceTests exercises this
// service's own validation-ordering and persistence logic in isolation. Real geography data
// (via the actual GeographyQueryService against seeded Kathmandu rows) is exercised end-to-end
// in Task 10's Api.Tests, mirroring how Branch's own geography validation was unit-tested with
// a stub and proven for real only at the Api.Tests layer in Phase 3.
file class StubGeographyLookupService : IGeographyLookupService
{
    private readonly bool _result;
    public StubGeographyLookupService(bool result) => _result = result;
    public Task<bool> IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid localLevelId, int wardNumber, CancellationToken cancellationToken = default)
        => Task.FromResult(_result);
    public Task<bool> DeliveryZoneExistsAsync(Guid zoneId, CancellationToken cancellationToken = default)
        => Task.FromResult(_result);
}

public class MerchantOnboardingServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MerchantConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantUserConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantAddressConfiguration());
        }
    }

    private static TestDbContext CreateContext() => new(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", userId.ToString())
    }, "test"));

    private static CreateMerchantRequest ValidRequest(string ownerEmail) => new(
        "Acme Traders Pvt. Ltd.", "Acme Traders", "01-4444444", "contact@acme.example",
        ownerEmail, "P@ssw0rd123!", "Owner Person",
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "New Road, Kathmandu");

    [Fact]
    public async Task CreateMerchantAsync_ValidGeography_PersistsMerchantOwnerAndAddress()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = new MerchantOnboardingService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new MerchantPasswordHasherAdapter(), new StubGeographyLookupService(true));

        var result = await service.CreateMerchantAsync(ValidRequest("owner1@example.com"), ActingUser(Guid.NewGuid()));

        Assert.Equal("Acme Traders Pvt. Ltd.", result.LegalName);
        Assert.Equal(KycStatus.Pending, result.KycStatus);
        Assert.True(result.IsActive);

        var owner = await dbContext.Set<MerchantUser>().SingleAsync(mu => mu.MerchantId == result.Id);
        Assert.Equal(MerchantUserRole.Owner, owner.Role);
        Assert.Equal("owner1@example.com", owner.Email);

        var address = await dbContext.Set<MerchantAddress>().SingleAsync(a => a.MerchantId == result.Id);
        Assert.True(address.IsDefaultPickup);
    }

    [Fact]
    public async Task CreateMerchantAsync_InvalidGeography_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var service = new MerchantOnboardingService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new MerchantPasswordHasherAdapter(), new StubGeographyLookupService(false));

        await Assert.ThrowsAsync<MerchantValidationException>(
            () => service.CreateMerchantAsync(ValidRequest("owner2@example.com"), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateMerchantAsync_DuplicateOwnerEmail_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var service = new MerchantOnboardingService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new MerchantPasswordHasherAdapter(), new StubGeographyLookupService(true));
        await service.CreateMerchantAsync(ValidRequest("dup@example.com"), ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<MerchantValidationException>(
            () => service.CreateMerchantAsync(ValidRequest("dup@example.com"), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task GetMerchantAsync_ThenListMerchantsAsync_ReturnsCreatedMerchant()
    {
        await using var dbContext = CreateContext();
        var service = new MerchantOnboardingService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new MerchantPasswordHasherAdapter(), new StubGeographyLookupService(true));
        var created = await service.CreateMerchantAsync(ValidRequest("owner3@example.com"), ActingUser(Guid.NewGuid()));

        var fetched = await service.GetMerchantAsync(created.Id);
        Assert.Equal(created.Id, fetched!.Id);

        var listed = await service.ListMerchantsAsync();
        Assert.Contains(listed, m => m.Id == created.Id);
    }
}
