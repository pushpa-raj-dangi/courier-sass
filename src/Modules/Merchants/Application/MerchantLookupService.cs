using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Application;

public class MerchantLookupService : IMerchantLookupService
{
    private readonly DbContext _dbContext;

    public MerchantLookupService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Merchant>().AnyAsync(m => m.Id == merchantId, cancellationToken);
}
