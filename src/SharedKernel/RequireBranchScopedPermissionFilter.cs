using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Courier.SharedKernel;

public class RequireBranchScopedPermissionFilter : IEndpointFilter
{
    private readonly string _permission;
    private readonly string _routeParameterName;

    public RequireBranchScopedPermissionFilter(string permission, string routeParameterName)
    {
        _permission = permission;
        _routeParameterName = routeParameterName;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (!httpContext.Request.RouteValues.TryGetValue(_routeParameterName, out var routeValue) ||
            !Guid.TryParse(routeValue?.ToString(), out var branchId))
        {
            return Results.BadRequest(new
            {
                error = new
                {
                    code = "INVALID_BRANCH_ID",
                    message = $"Route parameter '{_routeParameterName}' is not a valid branch id."
                }
            });
        }

        var checker = httpContext.RequestServices.GetRequiredService<IBranchAuthorizationChecker>();
        var hasAccess = await checker.HasAccessAsync(httpContext.User, _permission, branchId, httpContext.RequestAborted);
        if (!hasAccess)
        {
            return Results.Forbid();
        }

        return await next(context);
    }
}

public static class BranchAccessEndpointExtensions
{
    public static RouteHandlerBuilder RequireBranchScopedPermission(this RouteHandlerBuilder builder, string permission, string routeParameterName = "id")
    {
        return builder
            .RequireAuthorization()
            .AddEndpointFilter(new RequireBranchScopedPermissionFilter(permission, routeParameterName));
    }
}
