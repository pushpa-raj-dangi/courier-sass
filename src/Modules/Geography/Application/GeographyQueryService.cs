using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Geography.Application;

public class GeographyQueryService : IGeographyLookupService
{
    private readonly DbContext _dbContext;

    public GeographyQueryService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ProvinceResponse>> ListProvincesAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Province>()
            .OrderBy(p => p.Name)
            .Select(p => new ProvinceResponse(p.Id, p.Name))
            .ToListAsync(cancellationToken);

    public async Task<List<DistrictResponse>> ListDistrictsAsync(Guid provinceId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<District>()
            .Where(d => d.ProvinceId == provinceId)
            .OrderBy(d => d.Name)
            .Select(d => new DistrictResponse(d.Id, d.ProvinceId, d.Name))
            .ToListAsync(cancellationToken);

    public async Task<List<LocalLevelResponse>> ListLocalLevelsAsync(Guid districtId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<LocalLevel>()
            .Where(l => l.DistrictId == districtId)
            .OrderBy(l => l.Name)
            .Select(l => new LocalLevelResponse(l.Id, l.DistrictId, l.Name, l.Type))
            .ToListAsync(cancellationToken);

    public async Task<List<WardResponse>> ListWardsAsync(Guid localLevelId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Ward>()
            .Where(w => w.LocalLevelId == localLevelId)
            .OrderBy(w => w.Number)
            .Select(w => new WardResponse(w.Id, w.LocalLevelId, w.Number))
            .ToListAsync(cancellationToken);

    public async Task<List<AreaResponse>> ListAreasAsync(Guid wardId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Area>()
            .Where(a => a.WardId == wardId)
            .OrderBy(a => a.Name)
            .Select(a => new AreaResponse(a.Id, a.WardId, a.Name))
            .ToListAsync(cancellationToken);

    public async Task<bool> IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid localLevelId, int wardNumber, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Ward>()
            .Where(w => w.LocalLevelId == localLevelId && w.Number == wardNumber)
            .Join(_dbContext.Set<LocalLevel>(), w => w.LocalLevelId, l => l.Id, (w, l) => l)
            .Where(l => l.DistrictId == districtId)
            .Join(_dbContext.Set<District>(), l => l.DistrictId, d => d.Id, (l, d) => d)
            .AnyAsync(d => d.ProvinceId == provinceId, cancellationToken);
    }

    public async Task<bool> DeliveryZoneExistsAsync(Guid zoneId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<DeliveryZone>().AnyAsync(z => z.Id == zoneId, cancellationToken);
}
