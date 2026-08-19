using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Courier.Merchants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Merchants.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class MerchantStaffServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MerchantUserConfiguration());
        }
    }

    private static TestDbContext CreateContext() => new(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static ClaimsPrincipal OwnerPrincipal(Guid userId, Guid merchantId) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", userId.ToString()),
        new Claim("merchant_id", merchantId.ToString())
    }, "test"));

    private static async Task<MerchantUser> SeedOwnerAsync(TestDbContext dbContext, Guid merchantId, Guid organizationId)
    {
        var owner = new MerchantUser
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            OrganizationId = organizationId,
            Email = $"owner-{Guid.NewGuid():N}@example.com",
            PasswordHash = "not-used",
            FullName = "Owner Person",
            Role = MerchantUserRole.Owner,
            IsActive = true
        };
        dbContext.Set<MerchantUser>().Add(owner);
        await dbContext.SaveChangesAsync();
        return owner;
    }

    [Fact]
    public async Task InviteStaffAsync_ValidRequest_CreatesStaffUnderSameMerchantAndOrg()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, organizationId);
        var service = new MerchantStaffService(dbContext, new NoopAuditLogger(), new MerchantPasswordHasherAdapter());

        var result = await service.InviteStaffAsync(
            new InviteMerchantStaffRequest("staff@example.com", "P@ssw0rd123!", "Staff Person"),
            OwnerPrincipal(owner.Id, merchantId));

        Assert.Equal(MerchantUserRole.Staff, result.Role);
        var staffRow = await dbContext.Set<MerchantUser>().SingleAsync(mu => mu.Id == result.Id);
        Assert.Equal(merchantId, staffRow.MerchantId);
        Assert.Equal(organizationId, staffRow.OrganizationId);
    }

    [Fact]
    public async Task InviteStaffAsync_DuplicateEmail_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid());
        var service = new MerchantStaffService(dbContext, new NoopAuditLogger(), new MerchantPasswordHasherAdapter());
        await service.InviteStaffAsync(new InviteMerchantStaffRequest("dup-staff@example.com", "P@ssw0rd123!", "Staff Person"), OwnerPrincipal(owner.Id, merchantId));

        await Assert.ThrowsAsync<MerchantValidationException>(() =>
            service.InviteStaffAsync(new InviteMerchantStaffRequest("dup-staff@example.com", "P@ssw0rd123!", "Second Staff"), OwnerPrincipal(owner.Id, merchantId)));
    }

    [Fact]
    public async Task ListStaffAsync_ReturnsOnlyStaffForThatMerchant()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid());
        await SeedOwnerAsync(dbContext, otherMerchantId, Guid.NewGuid());
        var service = new MerchantStaffService(dbContext, new NoopAuditLogger(), new MerchantPasswordHasherAdapter());
        await service.InviteStaffAsync(new InviteMerchantStaffRequest("listed-staff@example.com", "P@ssw0rd123!", "Staff Person"), OwnerPrincipal(owner.Id, merchantId));

        var listed = await service.ListStaffAsync(OwnerPrincipal(owner.Id, merchantId));

        Assert.Contains(listed, mu => mu.Email == "listed-staff@example.com");
        Assert.DoesNotContain(listed, mu => mu.Email != "listed-staff@example.com" && mu.Role == MerchantUserRole.Owner && mu.Id != owner.Id);
    }
}
