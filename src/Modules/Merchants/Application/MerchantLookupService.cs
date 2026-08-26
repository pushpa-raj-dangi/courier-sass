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

    public async Task<string?> GetContactEmailAsync(Guid merchantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Merchant>().Where(m => m.Id == merchantId).Select(m => m.ContactEmail).FirstOrDefaultAsync(cancellationToken);
}
