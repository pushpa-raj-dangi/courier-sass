using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Courier.SharedKernel;

public class RequirePermissionFilter : IEndpointFilter
{
    private readonly string _permission;

    public RequirePermissionFilter(string permission)
    {
        _permission = permission;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var checker = httpContext.RequestServices.GetRequiredService<IPermissionChecker>();

        var hasPermission = await checker.HasPermissionAsync(httpContext.User, _permission, httpContext.RequestAborted);
        if (!hasPermission)
        {
            return Results.Forbid();
        }

        return await next(context);
    }
}

public static class PermissionEndpointExtensions
{
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission)
    {
        return builder
            .RequireAuthorization()
            .AddEndpointFilter(new RequirePermissionFilter(permission));
    }
}
