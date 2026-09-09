using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Courier.SharedKernel;

public class RequireSuperAdminFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.User.IsSuperAdmin())
        {
            return Results.Forbid();
        }
        return await next(context);
    }
}

public static class SuperAdminEndpointExtensions
{
    public static RouteHandlerBuilder RequireSuperAdmin(this RouteHandlerBuilder builder)
        => builder.RequireAuthorization().AddEndpointFilter(new RequireSuperAdminFilter());
}
