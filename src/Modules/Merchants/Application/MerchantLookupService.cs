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

    public async Task<string?> GetContactEmailAsync(Guid organizationId, Guid merchantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Merchant>().IgnoreQueryFilters()
            .Where(m => m.Id == merchantId && m.OrganizationId == organizationId)
            .Select(m => m.ContactEmail)
            .FirstOrDefaultAsync(cancellationToken);
}
