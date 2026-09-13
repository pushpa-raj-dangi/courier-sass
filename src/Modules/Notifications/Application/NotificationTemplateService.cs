using Courier.Audit.Application;
using Courier.Notifications.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Notifications.Application;

public class NotificationTemplateService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;

    public NotificationTemplateService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
    }

    public async Task<IReadOnlyList<NotificationTemplateDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new NotificationValidationException("No active organization context.");
        return await _dbContext.Set<NotificationTemplate>()
            .Where(t => t.OrganizationId == organizationId)
            .Select(t => new NotificationTemplateDto(t.Id, t.EventType, t.Channel, t.Body, t.IsActive))
            .ToListAsync(cancellationToken);
    }

    // Looks up any existing row for (org, eventType, channel) regardless of IsActive and updates
    // it in place rather than always inserting — NotificationTemplateConfiguration's own index
    // comment notes this combination is deliberately not unique, and NotificationDispatchService
    // picks an arbitrary row via FirstOrDefaultAsync when more than one exists, so letting repeat
    // edits accumulate duplicate rows would make dispatch's chosen row nondeterministic.
    public async Task<NotificationTemplateDto> UpsertAsync(Guid actingUserId, string eventType, NotificationChannel channel, string body, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!NotificationDefaultTemplates.Email.ContainsKey(eventType))
        {
            throw new NotificationValidationException($"Unknown event type '{eventType}'.");
        }
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new NotificationValidationException("No active organization context.");

        var existing = await _dbContext.Set<NotificationTemplate>()
            .FirstOrDefaultAsync(t => t.OrganizationId == organizationId && t.EventType == eventType && t.Channel == channel, cancellationToken);

        NotificationTemplate template;
        if (existing is null)
        {
            template = new NotificationTemplate { Id = Guid.NewGuid(), OrganizationId = organizationId, EventType = eventType, Channel = channel, Body = body, IsActive = isActive };
            _dbContext.Set<NotificationTemplate>().Add(template);
        }
        else
        {
            existing.Body = body;
            existing.IsActive = isActive;
            template = existing;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "notification_template.upserted", "NotificationTemplate", template.Id.ToString(),
            After: System.Text.Json.JsonSerializer.Serialize(new { eventType, channel, body, isActive })), cancellationToken);

        return new NotificationTemplateDto(template.Id, template.EventType, template.Channel, template.Body, template.IsActive);
    }
}
