namespace Courier.Branches.Application;

public interface IBranchHierarchyService
{
    // Returns branchId itself plus every descendant branch id (walking ParentBranchId).
    // Scoped to the current tenant automatically via AppDbContext's query filter.
    Task<IReadOnlySet<Guid>> GetDescendantBranchIdsAsync(Guid branchId, CancellationToken cancellationToken = default);
}
