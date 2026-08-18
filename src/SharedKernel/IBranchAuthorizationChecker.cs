using System.Security.Claims;

namespace Courier.SharedKernel;

public interface IBranchAuthorizationChecker
{
    /// <summary>
    /// Returns the set of branch ids the user may access for <paramref name="permission"/>,
    /// or <c>null</c> when the user holds a permission-granting role with
    /// <c>BranchScope.AllBranches</c> — callers must treat <c>null</c> as "no branch filter
    /// applies", never as an empty set.
    /// </summary>
    Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default);

    Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default);
}
