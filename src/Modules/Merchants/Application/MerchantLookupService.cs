using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Application;

public class MerchantLookupService : IMerchantLookupService, IMerchantContactLookupService
{
    private readonly DbContext _dbContext;

    public MerchantLookupService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Merchant>().AnyAsync(m => m.Id == merchantId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default)
    {
        if (merchantIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }
        var idArray = merchantIds.ToArray();
        var rows = await _dbContext.Set<Merchant>()
            .Where(m => idArray.Contains(m.Id))
            .Select(m => new { m.Id, m.DisplayName })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id, r => r.DisplayName);
    }

    public async Task<string?> GetContactEmailAsync(Guid organizationId, Guid merchantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Merchant>().IgnoreQueryFilters()
            .Where(m => m.Id == merchantId && m.OrganizationId == organizationId)
            .Select(m => m.ContactEmail)
            .FirstOrDefaultAsync(cancellationToken);
}
