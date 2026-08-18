namespace Courier.Audit.Application;

public interface IAuditLogger
{
    Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
