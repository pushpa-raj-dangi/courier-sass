namespace Courier.Platform.Application;

public sealed record QueueSummary(string Name, long Messages, long MessagesReady, long MessagesUnacknowledged, int Consumers, string State);

public sealed class PlatformQueuesService
{
    private readonly IRabbitMqManagementClient _client;

    public PlatformQueuesService(IRabbitMqManagementClient client)
    {
        _client = client;
    }

    public Task<IReadOnlyList<QueueSummary>> GetQueuesAsync(CancellationToken cancellationToken = default) =>
        _client.GetQueuesAsync(cancellationToken);
}
