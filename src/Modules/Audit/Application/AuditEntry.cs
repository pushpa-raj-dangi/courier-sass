namespace Courier.Audit.Application;

public record AuditEntry(
    Guid? OrganizationId,
    Guid? UserId,
    string Action,
    string EntityType,
    string EntityId,
    string? Before = null,
    string? After = null,
    string? IpAddress = null,
    string? UserAgent = null,
    bool IsCrossTenantAction = false);
