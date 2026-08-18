namespace Courier.Branches.Application;

public interface IBranchHierarchyService
{
    // Returns branchId itself plus every descendant branch id (walking ParentBranchId).
    // Scoped to the current tenant automatically via AppDbContext's query filter.
    Task<IReadOnlySet<Guid>> GetDescendantBranchIdsAsync(Guid branchId, CancellationToken cancellationToken = default);

    // Batched form: loads the tenant's branch tree once and BFS-expands from every seed id,
    // instead of one full-table load per seed. Prefer this whenever expanding more than one id.
    Task<IReadOnlySet<Guid>> GetDescendantBranchIdsAsync(IReadOnlyCollection<Guid> branchIds, CancellationToken cancellationToken = default);
}
