using Courier.Audit.Application;
using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Courier.Merchants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courier.Merchants.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class MerchantAuthServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MerchantConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantUserConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantRefreshTokenConfiguration());
        }
    }

    private static TestDbContext CreateContext() => new(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static MerchantAuthService CreateService(TestDbContext dbContext)
    {
        var jwtOptions = Options.Create(new MerchantJwtOptions
        {
            SigningKey = "merchant-tests-signing-key-at-least-32-bytes!",
            Issuer = "courier-tests",
            Audience = "courier-tests"
        });
        return new MerchantAuthService(dbContext, new MerchantPasswordHasherAdapter(), new MerchantJwtTokenService(jwtOptions), new NoopAuditLogger());
    }

    private static async Task<MerchantUser> SeedOwnerAsync(TestDbContext dbContext, string email, string password, bool merchantIsActive = true)
    {
        var merchant = new Merchant
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            LegalName = "Test Merchant Legal Name",
            DisplayName = "Test Merchant",
            IsActive = merchantIsActive
        };
        dbContext.Set<Merchant>().Add(merchant);

        var owner = new MerchantUser
        {
            Id = Guid.NewGuid(),
            MerchantId = merchant.Id,
            OrganizationId = merchant.OrganizationId,
            Email = email,
            PasswordHash = new MerchantPasswordHasherAdapter().Hash(password),
            FullName = "Owner Person",
            Role = MerchantUserRole.Owner,
            IsActive = true
        };
        dbContext.Set<MerchantUser>().Add(owner);
        await dbContext.SaveChangesAsync();
        return owner;
    }

    [Fact]
    public async Task LoginAsync_CorrectPassword_ReturnsTokensForMerchantUser()
    {
        await using var dbContext = CreateContext();
        var owner = await SeedOwnerAsync(dbContext, "owner@example.com", "P@ssw0rd123!");
        var service = CreateService(dbContext);

        var result = await service.LoginAsync(new MerchantLoginRequest("owner@example.com", "P@ssw0rd123!"));

        Assert.Equal(owner.Id, result.MerchantUserId);
        Assert.Equal(owner.MerchantId, result.MerchantId);
        Assert.Equal(owner.OrganizationId, result.OrganizationId);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsMerchantAuthenticationFailed()
    {
        await using var dbContext = CreateContext();
        await SeedOwnerAsync(dbContext, "owner2@example.com", "P@ssw0rd123!");
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<MerchantAuthenticationFailedException>(
            () => service.LoginAsync(new MerchantLoginRequest("owner2@example.com", "wrong-password")));
    }

    [Fact]
    public async Task LoginAsync_UnknownEmail_ThrowsMerchantAuthenticationFailed()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<MerchantAuthenticationFailedException>(
            () => service.LoginAsync(new MerchantLoginRequest("nobody@example.com", "whatever")));
    }

    [Fact]
    public async Task LoginAsync_InactiveMerchantUser_ThrowsMerchantAuthenticationFailed()
    {
        await using var dbContext = CreateContext();
        var owner = await SeedOwnerAsync(dbContext, "inactive@example.com", "P@ssw0rd123!");
        owner.IsActive = false;
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<MerchantAuthenticationFailedException>(
            () => service.LoginAsync(new MerchantLoginRequest("inactive@example.com", "P@ssw0rd123!")));
    }

    [Fact]
    public async Task LoginAsync_ActiveMerchantUserUnderInactiveMerchant_ThrowsMerchantAuthenticationFailed()
    {
        await using var dbContext = CreateContext();
        await SeedOwnerAsync(dbContext, "inactive-merchant@example.com", "P@ssw0rd123!", merchantIsActive: false);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<MerchantAuthenticationFailedException>(
            () => service.LoginAsync(new MerchantLoginRequest("inactive-merchant@example.com", "P@ssw0rd123!")));
    }

    [Fact]
    public async Task RefreshTokenAsync_RotatesToken()
    {
        await using var dbContext = CreateContext();
        await SeedOwnerAsync(dbContext, "refresh@example.com", "P@ssw0rd123!");
        var service = CreateService(dbContext);
        var loginResult = await service.LoginAsync(new MerchantLoginRequest("refresh@example.com", "P@ssw0rd123!"));

        var refreshed = await service.RefreshTokenAsync(new MerchantRefreshRequest(loginResult.RefreshToken));

        Assert.Equal(loginResult.MerchantUserId, refreshed.MerchantUserId);
        Assert.NotEqual(loginResult.RefreshToken, refreshed.RefreshToken);

        await Assert.ThrowsAsync<MerchantAuthenticationFailedException>(
            () => service.RefreshTokenAsync(new MerchantRefreshRequest(loginResult.RefreshToken)));
    }
}
