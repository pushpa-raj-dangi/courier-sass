namespace Courier.Platform.Application;

public interface IRabbitMqManagementClient
{
    Task<IReadOnlyList<QueueSummary>> GetQueuesAsync(CancellationToken cancellationToken = default);
}
