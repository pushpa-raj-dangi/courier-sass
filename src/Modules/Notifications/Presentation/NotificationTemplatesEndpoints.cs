using Courier.Notifications.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Notifications.Presentation;

public record UpsertNotificationTemplateRequest(string EventType, Courier.Notifications.Domain.NotificationChannel Channel, string Body, bool IsActive, string? Locale = null);

public static class NotificationTemplatesEndpoints
{
    public static void MapNotificationTemplatesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/settings/notification-templates").WithTags("Notification Templates");

        group.MapGet("/", async (NotificationTemplateService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken))).RequirePermission("notification_template.manage");

        group.MapPut("/", async (UpsertNotificationTemplateRequest request, NotificationTemplateService service, HttpContext httpContext) =>
        {
            var actingUserId = httpContext.User.GetUserId()!.Value;
            try
            {
                return Results.Ok(await service.UpsertAsync(actingUserId, request.EventType, request.Channel, request.Body, request.IsActive, request.Locale, httpContext.RequestAborted));
            }
            catch (NotificationValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "NOTIFICATION_TEMPLATE_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("notification_template.manage");
    }
}
