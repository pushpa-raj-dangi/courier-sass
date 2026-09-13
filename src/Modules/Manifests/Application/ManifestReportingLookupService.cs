using Courier.Manifests.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Manifests.Application;

public class ManifestReportingLookupService : IManifestReportingLookupService
{
    private readonly DbContext _dbContext;

    public ManifestReportingLookupService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BranchManifestCounts> GetBranchManifestCountsAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var inTransitTowardBranch = await _dbContext.Set<Manifest>().AsNoTracking()
            .CountAsync(m => m.ToBranchId == branchId && m.Status == ManifestStatus.InTransit, cancellationToken);

        // No ClosedAt column exists on Manifest — the latest ManifestShipment.ReceivedAt across a
        // Closed manifest's rows is used as a proxy for "when it was closed" (this plan's resolved
        // design decision, since receiving is what drives a manifest to Closed in the first place).
        var recentCutoff = DateTimeOffset.UtcNow.AddHours(-24);
        var recentlyClosed = await _dbContext.Set<Manifest>().AsNoTracking()
            .Where(m => m.ToBranchId == branchId && m.Status == ManifestStatus.Closed)
            .Where(m => _dbContext.Set<ManifestShipment>().AsNoTracking()
                .Any(ms => ms.ManifestId == m.Id && ms.ReceivedAt != null && ms.ReceivedAt >= recentCutoff))
            .CountAsync(cancellationToken);

        return new BranchManifestCounts(inTransitTowardBranch, recentlyClosed);
    }

    public async Task<int> GetOpenMissingShipmentExceptionCountAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var closedManifestIds = _dbContext.Set<Manifest>().AsNoTracking()
            .Where(m => m.ToBranchId == branchId && m.Status == ManifestStatus.Closed)
            .Select(m => m.Id);

        return await _dbContext.Set<ManifestShipment>().AsNoTracking()
            .CountAsync(ms => closedManifestIds.Contains(ms.ManifestId) && ms.ReceivedAt == null, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<ManifestStatus, int>> GetStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Manifest>().AsNoTracking().AsQueryable();
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(m => branchIdArray.Contains(m.FromBranchId) || branchIdArray.Contains(m.ToBranchId));
        }

        var counts = await query
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(x => x.Status, x => x.Count);
    }
}
