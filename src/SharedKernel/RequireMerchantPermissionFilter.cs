using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Courier.SharedKernel;

public class RequireMerchantPermissionFilter : IEndpointFilter
{
    private readonly string _permission;

    public RequireMerchantPermissionFilter(string permission)
    {
        _permission = permission;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var checker = httpContext.RequestServices.GetRequiredService<IMerchantAuthorizationChecker>();

        var hasPermission = await checker.HasPermissionAsync(httpContext.User, _permission, httpContext.RequestAborted);
        if (!hasPermission)
        {
            return Results.Forbid();
        }

        return await next(context);
    }
}

public static class MerchantPermissionEndpointExtensions
{
    public static RouteHandlerBuilder RequireMerchantPermission(this RouteHandlerBuilder builder, string permission)
    {
        return builder
            .RequireAuthorization()
            .AddEndpointFilter(new RequireMerchantPermissionFilter(permission));
    }
}
