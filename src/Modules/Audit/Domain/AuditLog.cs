namespace Courier.Audit.Domain;

public class AuditLog
{
    public Guid Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? UserId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public bool IsCrossTenantAction { get; set; }
}
