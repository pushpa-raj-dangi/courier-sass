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

        group.MapPatch("/security", async (SecurityPolicyDto request, OrganizationSettingsService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
        {
            var organizationId = tenantProvider.OrganizationId!.Value;
            var actingUserId = httpContext.User.GetUserId()!.Value;
            try
            {
                return Results.Ok(await service.UpdateSecurityAsync(organizationId, actingUserId, request, httpContext.RequestAborted));
            }
            catch (OrganizationSettingsValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "ORGANIZATION_SETTINGS_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("org.settings.manage");

        group.MapPost("/revoke-sessions", async (OrganizationSettingsService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
        {
            var organizationId = tenantProvider.OrganizationId!.Value;
            var actingUserId = httpContext.User.GetUserId()!.Value;
            await service.RevokeAllSessionsAsync(organizationId, actingUserId, httpContext.RequestAborted);
            return Results.Ok();
        }).RequirePermission("org.settings.manage");

        // Narrower than GET "/": exposes only OperationalDefaultsDto (no profile/security data) and
        // is gated on shipment.create rather than org.settings.manage, so every shipment-creating
        // role (CounterStaff, OperationsManager) can pre-fill the shipment-creation form without
        // being granted the full Settings-management permission.
        group.MapGet("/defaults", async (OrganizationSettingsService service, ICurrentTenantProvider tenantProvider, CancellationToken cancellationToken) =>
        {
            var organizationId = tenantProvider.OrganizationId!.Value;
            var settings = await service.GetAsync(organizationId, cancellationToken);
            return Results.Ok(settings.Defaults);
        }).RequirePermission("shipment.create");

        group.MapPatch("/defaults", async (OperationalDefaultsDto request, OrganizationSettingsService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
        {
            var organizationId = tenantProvider.OrganizationId!.Value;
            var actingUserId = httpContext.User.GetUserId()!.Value;
            try
            {
                return Results.Ok(await service.UpdateDefaultsAsync(organizationId, actingUserId, request, httpContext.RequestAborted));
            }
            catch (OrganizationSettingsValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "ORGANIZATION_SETTINGS_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("org.settings.manage");
    }
}
