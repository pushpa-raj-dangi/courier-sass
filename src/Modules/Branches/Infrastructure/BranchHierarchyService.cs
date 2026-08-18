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

    public async Task<IReadOnlySet<Guid>> GetDescendantBranchIdsAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var branches = await _dbContext.Set<Branch>()
            .Select(b => new { b.Id, b.ParentBranchId })
            .ToListAsync(cancellationToken);

        var childrenByParent = branches
            .Where(b => b.ParentBranchId is not null)
            .GroupBy(b => b.ParentBranchId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(b => b.Id).ToList());

        var result = new HashSet<Guid> { branchId };
        var queue = new Queue<Guid>();
        queue.Enqueue(branchId);
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
