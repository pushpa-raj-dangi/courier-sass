using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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

public class ApiKeyServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MerchantUserConfiguration());
            modelBuilder.ApplyConfiguration(new ApiKeyConfiguration());
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

    private static async Task<MerchantUser> SeedOwnerAsync(TestDbContext dbContext, Guid merchantId, Guid organizationId, MerchantUserRole role = MerchantUserRole.Owner)
    {
        var owner = new MerchantUser
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            OrganizationId = organizationId,
            Email = $"owner-{Guid.NewGuid():N}@example.com",
            PasswordHash = "not-used",
            FullName = "Owner Person",
            Role = role,
            IsActive = true
        };
        dbContext.Set<MerchantUser>().Add(owner);
        await dbContext.SaveChangesAsync();
        return owner;
    }

    [Fact]
    public async Task CreateApiKeyAsync_NoScopesRequested_DefaultsToAllOfCreatorsPermissionsExceptApiKeysManage()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid());
        var service = new ApiKeyService(dbContext, new NoopAuditLogger());

        var result = await service.CreateApiKeyAsync(new CreateApiKeyRequest(), OwnerPrincipal(owner.Id, merchantId));

        var expected = MerchantPermissionCatalog.RolePermissions[MerchantUserRole.Owner]
            .Where(s => s != MerchantPermissionCatalog.ApiKeysManage)
            .OrderBy(s => s);
        Assert.Equal(expected, result.Scopes.OrderBy(s => s));
    }

    // Defense-in-depth for the "an API key can never manage other API keys" carve-out: an Owner
    // holds merchant.apikeys.manage, but a key defaulted from that Owner's permissions must not
    // carry it, so the carve-out no longer rests solely on ApiKeyEndpoints staying JWT-only.
    [Fact]
    public async Task CreateApiKeyAsync_NoScopesRequested_NeverGrantsApiKeysManage()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid());
        var service = new ApiKeyService(dbContext, new NoopAuditLogger());

        var result = await service.CreateApiKeyAsync(new CreateApiKeyRequest(), OwnerPrincipal(owner.Id, merchantId));

        // The Owner role genuinely holds it — this is not a vacuous assertion.
        Assert.Contains(MerchantPermissionCatalog.ApiKeysManage, MerchantPermissionCatalog.RolePermissions[MerchantUserRole.Owner]);
        Assert.DoesNotContain(MerchantPermissionCatalog.ApiKeysManage, result.Scopes);
        var stored = await dbContext.Set<ApiKey>().SingleAsync(k => k.Id == result.Id);
        Assert.DoesNotContain(MerchantPermissionCatalog.ApiKeysManage, stored.Scopes);
    }

    // Explicitly asking for it fails loudly rather than being silently stripped — the caller asked
    // for something that will never be honoured.
    [Fact]
    public async Task CreateApiKeyAsync_ExplicitlyRequestsApiKeysManage_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid());
        var service = new ApiKeyService(dbContext, new NoopAuditLogger());

        var exception = await Assert.ThrowsAsync<MerchantValidationException>(() =>
            service.CreateApiKeyAsync(
                new CreateApiKeyRequest(new[] { MerchantPermissionCatalog.ShipmentRead, MerchantPermissionCatalog.ApiKeysManage }),
                OwnerPrincipal(owner.Id, merchantId)));

        Assert.Contains(MerchantPermissionCatalog.ApiKeysManage, exception.Message);
        Assert.Empty(await dbContext.Set<ApiKey>().ToListAsync());
    }

    [Fact]
    public async Task CreateApiKeyAsync_ScopeExceedsCreatorsPermissions_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var staff = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid(), MerchantUserRole.Staff);
        var service = new ApiKeyService(dbContext, new NoopAuditLogger());

        await Assert.ThrowsAsync<MerchantValidationException>(() =>
            service.CreateApiKeyAsync(new CreateApiKeyRequest(new[] { MerchantPermissionCatalog.StaffManage }), OwnerPrincipal(staff.Id, merchantId)));
    }

    [Fact]
    public async Task CreateApiKeyAsync_ReturnedPlaintextSecretHashesToStoredHashedKey()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid());
        var service = new ApiKeyService(dbContext, new NoopAuditLogger());

        var result = await service.CreateApiKeyAsync(new CreateApiKeyRequest(), OwnerPrincipal(owner.Id, merchantId));
        var stored = await dbContext.Set<ApiKey>().SingleAsync(k => k.Id == result.Id);
        var secret = result.PlaintextKey.Split('.', 2)[1];

        Assert.Equal(stored.Prefix, result.Prefix);
        Assert.Equal(stored.HashedKey, Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
    }

    [Fact]
    public async Task ListApiKeysAsync_ReturnsOnlyKeysForThatMerchant()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid());
        var otherOwner = await SeedOwnerAsync(dbContext, otherMerchantId, Guid.NewGuid());
        var service = new ApiKeyService(dbContext, new NoopAuditLogger());
        await service.CreateApiKeyAsync(new CreateApiKeyRequest(), OwnerPrincipal(owner.Id, merchantId));
        await service.CreateApiKeyAsync(new CreateApiKeyRequest(), OwnerPrincipal(otherOwner.Id, otherMerchantId));

        var listed = await service.ListApiKeysAsync(OwnerPrincipal(owner.Id, merchantId));

        Assert.Single(listed);
    }

    [Fact]
    public async Task RevokeApiKeyAsync_OwnKey_SetsInactiveAndRevokedAt()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid());
        var service = new ApiKeyService(dbContext, new NoopAuditLogger());
        var created = await service.CreateApiKeyAsync(new CreateApiKeyRequest(), OwnerPrincipal(owner.Id, merchantId));

        var revoked = await service.RevokeApiKeyAsync(created.Id, OwnerPrincipal(owner.Id, merchantId));

        Assert.NotNull(revoked);
        Assert.False(revoked!.IsActive);
        Assert.NotNull(revoked.RevokedAt);
    }

    [Fact]
    public async Task RevokeApiKeyAsync_KeyBelongsToAnotherMerchant_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid());
        var otherOwner = await SeedOwnerAsync(dbContext, otherMerchantId, Guid.NewGuid());
        var service = new ApiKeyService(dbContext, new NoopAuditLogger());
        var created = await service.CreateApiKeyAsync(new CreateApiKeyRequest(), OwnerPrincipal(otherOwner.Id, otherMerchantId));

        var result = await service.RevokeApiKeyAsync(created.Id, OwnerPrincipal(owner.Id, merchantId));

        Assert.Null(result);
    }
}
