using System.Security.Claims;
using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Infrastructure;

public class MerchantAuthorizationChecker : IMerchantAuthorizationChecker
{
    private readonly DbContext _dbContext;

    public MerchantAuthorizationChecker(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default)
    {
        // An ApiKey-authenticated principal (Task 5) carries its granted scopes directly on the
        // claim, not a MerchantUser row — checked here and nowhere else, so both credential types
        // share this one entry point.
        var scopesClaim = user.FindFirst("api_key_scopes")?.Value;
        if (scopesClaim is not null)
        {
            // The owning Merchant must still be active for an API-key credential too — the same
            // invariant the MerchantUser path below enforces. Declaring it for only one credential
            // type would mean a future merchant-deactivation feature silently leaves every issued
            // API key working.
            var apiKeyMerchantId = user.GetMerchantId();
            if (apiKeyMerchantId is null)
            {
                return false;
            }

            var apiKeyMerchant = await _dbContext.Set<Merchant>()
                .FirstOrDefaultAsync(m => m.Id == apiKeyMerchantId, cancellationToken);
            if (apiKeyMerchant is null || !apiKeyMerchant.IsActive)
            {
                return false;
            }

            return scopesClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(permission);
        }

        var merchantUserId = user.GetUserId();
        if (merchantUserId is null)
        {
            return false;
        }

        var merchantUser = await _dbContext.Set<MerchantUser>()
            .FirstOrDefaultAsync(mu => mu.Id == merchantUserId && mu.IsActive, cancellationToken);
        if (merchantUser is null)
        {
            return false;
        }

        var merchant = await _dbContext.Set<Merchant>()
            .FirstOrDefaultAsync(m => m.Id == merchantUser.MerchantId, cancellationToken);
        if (merchant is null || !merchant.IsActive)
        {
            return false;
        }

        return MerchantPermissionCatalog.RolePermissions[merchantUser.Role].Contains(permission);
    }
}
