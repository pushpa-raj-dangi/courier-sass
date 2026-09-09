using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Application;

public class MerchantSessionRevocationService : IMerchantSessionRevoker
{
    private readonly DbContext _dbContext;

    public MerchantSessionRevocationService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // IgnoreQueryFilters(): called only from Platform's OrganizationLifecycleService, which
    // always runs with ICurrentTenantProvider.OrganizationId == null (SuperAdmin, cross-tenant) —
    // the ambient MerchantUser query filter fails closed in that situation and would otherwise
    // silently revoke nothing.
    public async Task RevokeAllForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var merchantUserIds = await _dbContext.Set<MerchantUser>().IgnoreQueryFilters()
            .Where(mu => mu.OrganizationId == organizationId)
            .Select(mu => mu.Id)
            .ToListAsync(cancellationToken);
        var tokens = await _dbContext.Set<MerchantRefreshToken>()
            .Where(rt => merchantUserIds.Contains(rt.MerchantUserId) && rt.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
