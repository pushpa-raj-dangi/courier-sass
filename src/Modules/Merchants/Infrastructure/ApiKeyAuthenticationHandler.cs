using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Courier.Merchants.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Courier.Merchants.Infrastructure;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly DbContext _dbContext;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        DbContext dbContext) : base(options, logger, encoder)
    {
        _dbContext = dbContext;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var raw = ExtractCredential(Request);
        if (raw is null)
        {
            return AuthenticateResult.NoResult();
        }

        var parts = raw.Split('.', 2);
        if (parts.Length != 2)
        {
            return AuthenticateResult.Fail("API_KEY_INVALID");
        }
        var (prefix, secret) = (parts[0], parts[1]);

        // No tenant context exists yet at this point in the pipeline (this handler runs before
        // AppDbContext's tenant filter has anything to key off) — IgnoreQueryFilters with an
        // explicit trusted predicate (unique Prefix) is the deliberate, documented exception from
        // Global Constraints, same reasoning as public tracking (Task 12).
        var apiKey = await _dbContext.Set<ApiKey>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.Prefix == prefix);
        // API_KEY_INVALID = the credential does not exist / is malformed; API_KEY_REVOKED = it
        // existed but was explicitly revoked or deactivated. A prefix that was never issued was
        // never revoked, so the two cases are kept distinct.
        if (apiKey is null)
        {
            return AuthenticateResult.Fail("API_KEY_INVALID");
        }
        if (!apiKey.IsActive || apiKey.RevokedAt is not null)
        {
            return AuthenticateResult.Fail("API_KEY_REVOKED");
        }

        var hashedSecret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hashedSecret), Encoding.UTF8.GetBytes(apiKey.HashedKey)))
        {
            return AuthenticateResult.Fail("API_KEY_INVALID");
        }

        apiKey.LastUsedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Same claim types (sub, org_id, merchant_id) MerchantJwtTokenService.IssueAccessToken
        // already emits, so ClaimsPrincipalExtensions/HttpContextCurrentTenantProvider work
        // unchanged for either credential — api_key_scopes is the one addition, read only by
        // MerchantAuthorizationChecker's new branch (Step 4).
        var claims = new[]
        {
            new Claim("sub", apiKey.Id.ToString()),
            new Claim("org_id", apiKey.OrganizationId.ToString()),
            new Claim("merchant_id", apiKey.MerchantId.ToString()),
            new Claim("api_key_scopes", string.Join(' ', apiKey.Scopes))
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    private static string? ExtractCredential(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader) && !string.IsNullOrEmpty(apiKeyHeader))
        {
            return apiKeyHeader.ToString();
        }
        // A real JWT also arrives via "Authorization: Bearer <token>" and is picked up here too —
        // harmlessly. A JWT has 3 dot-separated segments, so Split('.', 2) below treats its
        // base64url header as "Prefix", which never matches a stored ApiKey.Prefix; this handler
        // then returns AuthenticateResult.Fail (not an exception), and the separately-registered
        // "Bearer" scheme authenticates the same header independently as a JWT.
        if (request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var value = authHeader.ToString();
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return value["Bearer ".Length..];
            }
        }
        return null;
    }
}
