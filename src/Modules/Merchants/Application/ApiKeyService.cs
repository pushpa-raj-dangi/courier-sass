using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Courier.Audit.Application;
using Courier.Merchants.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Application;

public class ApiKeyService
{
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;

    public ApiKeyService(DbContext dbContext, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
    }

    public async Task<ApiKeyCreatedResponse> CreateApiKeyAsync(CreateApiKeyRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var merchantId = actingUser.GetMerchantId()
            ?? throw new MerchantValidationException("Acting merchant could not be resolved.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new MerchantValidationException("Acting user could not be resolved.");

        var actingMerchantUser = await _dbContext.Set<MerchantUser>()
            .FirstOrDefaultAsync(mu => mu.Id == actingUserId, cancellationToken)
            ?? throw new MerchantValidationException("Acting merchant user could not be resolved.");

        var grantablePermissions = MerchantPermissionCatalog.RolePermissions[actingMerchantUser.Role];

        // An API key can never manage other API keys — key management is JWT-only by design
        // (ApiKeyEndpoints stays on the JWT-only RequireMerchantPermission extension). Enforcing
        // it here too means that carve-out no longer rests on a single layer: even if those routes
        // were ever widened to the any-credential extension, no issued key would hold the scope.
        // An explicit request is rejected loudly rather than silently stripped; the defaulted
        // grant just drops it.
        string[] scopes;
        if (request.Scopes is { Length: > 0 })
        {
            if (request.Scopes.Contains(MerchantPermissionCatalog.ApiKeysManage))
            {
                throw new MerchantValidationException($"API keys can never be granted {MerchantPermissionCatalog.ApiKeysManage} — key management is JWT-only by design.");
            }
            scopes = request.Scopes;
        }
        else
        {
            scopes = grantablePermissions.Where(p => p != MerchantPermissionCatalog.ApiKeysManage).ToArray();
        }

        var invalidScope = scopes.FirstOrDefault(s => !grantablePermissions.Contains(s));
        if (invalidScope is not null)
        {
            throw new MerchantValidationException($"Scope '{invalidScope}' exceeds the creating user's own permissions.");
        }

        var (prefix, secret) = await GenerateUniqueCredentialAsync(cancellationToken);
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            OrganizationId = actingMerchantUser.OrganizationId,
            HashedKey = HashSecret(secret),
            Prefix = prefix,
            Scopes = scopes,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Set<ApiKey>().Add(apiKey);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(actingMerchantUser.OrganizationId, actingUserId, "merchant_api_key.created", "ApiKey", apiKey.Id.ToString()), cancellationToken);

        return new ApiKeyCreatedResponse(apiKey.Id, $"{prefix}.{secret}", prefix, scopes, apiKey.CreatedAt);
    }

    public async Task<List<ApiKeyResponse>> ListApiKeysAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var merchantId = user.GetMerchantId()
            ?? throw new MerchantValidationException("Acting merchant could not be resolved.");

        var keys = await _dbContext.Set<ApiKey>()
            .Where(k => k.MerchantId == merchantId)
            .ToListAsync(cancellationToken);
        return keys.Select(ToResponse).ToList();
    }

    public async Task<ApiKeyResponse?> RevokeApiKeyAsync(Guid id, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var merchantId = actingUser.GetMerchantId()
            ?? throw new MerchantValidationException("Acting merchant could not be resolved.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new MerchantValidationException("Acting user could not be resolved.");

        var apiKey = await _dbContext.Set<ApiKey>()
            .FirstOrDefaultAsync(k => k.Id == id && k.MerchantId == merchantId, cancellationToken);
        if (apiKey is null)
        {
            return null;
        }

        apiKey.IsActive = false;
        apiKey.RevokedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(apiKey.OrganizationId, actingUserId, "merchant_api_key.revoked", "ApiKey", apiKey.Id.ToString()), cancellationToken);

        return ToResponse(apiKey);
    }

    // Deliberate IgnoreQueryFilters: Prefix uniqueness (ApiKeyConfiguration's unique index) is
    // global across all tenants, not per-org — a prefix collision check must see every org's
    // rows, not just the caller's, or two orgs could collide on the same prefix and break
    // ApiKeyAuthenticationHandler's (Task 5) O(1) lookup.
    private async Task<(string Prefix, string Secret)> GenerateUniqueCredentialAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var prefix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            var exists = await _dbContext.Set<ApiKey>().IgnoreQueryFilters().AnyAsync(k => k.Prefix == prefix, cancellationToken);
            if (!exists)
            {
                var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                return (prefix, secret);
            }
        }
        throw new MerchantValidationException("Could not generate a unique API key prefix. Please retry.");
    }

    private static string HashSecret(string secret) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static ApiKeyResponse ToResponse(ApiKey apiKey) => new(
        apiKey.Id, apiKey.Prefix, apiKey.Scopes, apiKey.IsActive, apiKey.CreatedAt, apiKey.RevokedAt, apiKey.LastUsedAt);
}
