using Courier.Platform.Application;
using Xunit;

namespace Courier.Platform.Tests;

public class PlatformQueuesServiceTests
{
    private sealed class FakeRabbitMqManagementClient : IRabbitMqManagementClient
    {
        public List<QueueSummary> Queues { get; set; } = new();

        public Task<IReadOnlyList<QueueSummary>> GetQueuesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QueueSummary>>(Queues);
    }

    [Fact]
    public async Task GetQueuesAsync_ReturnsExactlyWhatTheManagementClientReturns()
    {
        var client = new FakeRabbitMqManagementClient
        {
            Queues = new List<QueueSummary>
            {
                new("courier.notifications", Messages: 5, MessagesReady: 5, MessagesUnacknowledged: 0, Consumers: 1, State: "running"),
                new("courier.webhooks", Messages: 0, MessagesReady: 0, MessagesUnacknowledged: 0, Consumers: 0, State: "idle"),
            },
        };
        var service = new PlatformQueuesService(client);

        var result = await service.GetQueuesAsync();

        Assert.Equal(client.Queues, result);
    }
}
