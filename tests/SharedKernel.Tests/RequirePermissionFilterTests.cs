using System.Security.Claims;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class RequirePermissionFilterTests
{
    private sealed class FakePermissionChecker : IPermissionChecker
    {
        private readonly bool _result;
        public FakePermissionChecker(bool result) => _result = result;

        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private static EndpointFilterInvocationContext BuildContext(bool authenticated, IPermissionChecker checker)
    {
        var services = new ServiceCollection();
        services.AddSingleton(checker);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (authenticated)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "TestAuth"));
        }
        return new DefaultEndpointFilterInvocationContext(httpContext);
    }

    [Fact]
    public async Task InvokeAsync_UserHasPermission_CallsNext()
    {
        var filter = new RequirePermissionFilter("user.read");
        var context = BuildContext(authenticated: true, new FakePermissionChecker(true));
        var nextCalled = false;

        var result = await filter.InvokeAsync(context, _ => { nextCalled = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        Assert.True(nextCalled);
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
    }

    [Fact]
    public async Task InvokeAsync_UserLacksPermission_ReturnsForbid()
    {
        var filter = new RequirePermissionFilter("user.read");
        var context = BuildContext(authenticated: true, new FakePermissionChecker(false));
        var nextCalled = false;

        var result = await filter.InvokeAsync(context, _ => { nextCalled = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        Assert.False(nextCalled);
        Assert.IsType<ForbidHttpResult>(result);
    }
}
