using System.Security.Claims;

namespace Courier.SharedKernel;

public interface IBranchAuthorizationChecker
{
    // Null means the user holds a permission-granting role with BranchScope.AllBranches —
    // callers should treat this as "no branch filter applies", not as "empty set".
    Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default);

    Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default);
}
