using Courier.Identity.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Identity.Presentation;

public static class OrganizationSettingsEndpoints
{
    public static void MapOrganizationSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/settings/organization").WithTags("Organization Settings");

        group.MapGet("/", async (OrganizationSettingsService service, ICurrentTenantProvider tenantProvider, CancellationToken cancellationToken) =>
        {
            var organizationId = tenantProvider.OrganizationId!.Value;
            return Results.Ok(await service.GetAsync(organizationId, cancellationToken));
        }).RequirePermission("org.settings.manage");

        group.MapPatch("/profile", async (OrganizationProfileDto request, OrganizationSettingsService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
        {
            var organizationId = tenantProvider.OrganizationId!.Value;
            var actingUserId = httpContext.User.GetUserId()!.Value;
            try
            {
                return Results.Ok(await service.UpdateProfileAsync(organizationId, actingUserId, request, httpContext.RequestAborted));
            }
            catch (OrganizationSettingsValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "ORGANIZATION_SETTINGS_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("org.settings.manage");
    }
}
