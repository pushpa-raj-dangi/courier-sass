using Courier.Branches.Application;
using Courier.Branches.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Branches.Infrastructure;

public class BranchHierarchyService : IBranchHierarchyService
{
    private readonly DbContext _dbContext;

    public BranchHierarchyService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<IReadOnlySet<Guid>> GetDescendantBranchIdsAsync(Guid branchId, CancellationToken cancellationToken = default)
        => GetDescendantBranchIdsAsync(new[] { branchId }, cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetDescendantBranchIdsAsync(IReadOnlyCollection<Guid> branchIds, CancellationToken cancellationToken = default)
    {
        var branches = await _dbContext.Set<Branch>()
            .Select(b => new { b.Id, b.ParentBranchId })
            .ToListAsync(cancellationToken);

        var childrenByParent = branches
            .Where(b => b.ParentBranchId is not null)
            .GroupBy(b => b.ParentBranchId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(b => b.Id).ToList());

        var result = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        foreach (var seedId in branchIds)
        {
            if (result.Add(seedId))
            {
                queue.Enqueue(seedId);
            }
        }
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!childrenByParent.TryGetValue(current, out var children))
            {
                continue;
            }
            foreach (var childId in children)
            {
                if (result.Add(childId))
                {
                    queue.Enqueue(childId);
                }
            }
        }
        return result;
    }
}
