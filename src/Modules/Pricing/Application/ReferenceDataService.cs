using Courier.Pricing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Pricing.Application;

public class ReferenceDataService
{
    private readonly DbContext _dbContext;

    public ReferenceDataService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ServiceTypeResponse>> ListServiceTypesAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Set<ServiceType>()
            .OrderBy(s => s.Name)
            .Select(s => new ServiceTypeResponse(s.Id, s.Code, s.Name))
            .ToListAsync(cancellationToken);

    public async Task<List<WeightSlabResponse>> ListWeightSlabsAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Set<WeightSlab>()
            .OrderBy(w => w.MinWeightKg)
            .Select(w => new WeightSlabResponse(w.Id, w.MinWeightKg, w.MaxWeightKg))
            .ToListAsync(cancellationToken);
}
