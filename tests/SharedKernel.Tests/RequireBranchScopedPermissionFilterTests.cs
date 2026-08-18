using System.Security.Claims;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class RequireBranchScopedPermissionFilterTests
{
    private sealed class FakeBranchAuthorizationChecker : IBranchAuthorizationChecker
    {
        private readonly bool _result;
        public FakeBranchAuthorizationChecker(bool result) => _result = result;

        public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private static EndpointFilterInvocationContext BuildContext(Guid? routeBranchId, IBranchAuthorizationChecker checker)
    {
        var services = new ServiceCollection();
        services.AddSingleton(checker);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "TestAuth"));
        if (routeBranchId is not null)
        {
            httpContext.Request.RouteValues = new RouteValueDictionary { ["id"] = routeBranchId.Value.ToString() };
        }
        return new DefaultEndpointFilterInvocationContext(httpContext);
    }

    [Fact]
    public async Task InvokeAsync_UserHasBranchAccess_CallsNext()
    {
        var filter = new RequireBranchScopedPermissionFilter("branch.read", "id");
        var context = BuildContext(Guid.NewGuid(), new FakeBranchAuthorizationChecker(true));
        var nextCalled = false;

        var result = await filter.InvokeAsync(context, _ => { nextCalled = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        Assert.True(nextCalled);
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
    }

    [Fact]
    public async Task InvokeAsync_UserLacksBranchAccess_ReturnsForbid()
    {
        var filter = new RequireBranchScopedPermissionFilter("branch.read", "id");
        var context = BuildContext(Guid.NewGuid(), new FakeBranchAuthorizationChecker(false));

        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, statusResult.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_MissingRouteValue_ReturnsBadRequest()
    {
        var filter = new RequireBranchScopedPermissionFilter("branch.read", "id");
        var context = BuildContext(routeBranchId: null, new FakeBranchAuthorizationChecker(true));

        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, statusResult.StatusCode);
    }
}
