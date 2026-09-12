using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Courier.Platform.Application;

namespace Courier.Platform.Infrastructure;

public sealed class RabbitMqManagementClient : IRabbitMqManagementClient
{
    private readonly HttpClient _httpClient;

    public RabbitMqManagementClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<QueueSummary>> GetQueuesAsync(CancellationToken cancellationToken = default)
    {
        var queues = await _httpClient.GetFromJsonAsync<List<RabbitMqQueueResponse>>("api/queues/%2f", cancellationToken)
            ?? new List<RabbitMqQueueResponse>();

        return queues
            .Select(q => new QueueSummary(q.Name, q.Messages, q.MessagesReady, q.MessagesUnacknowledged, q.Consumers, q.State ?? "unknown"))
            .OrderBy(q => q.Name, StringComparer.Ordinal)
            .ToList();
    }

    private sealed class RabbitMqQueueResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("messages")]
        public long Messages { get; set; }

        [JsonPropertyName("messages_ready")]
        public long MessagesReady { get; set; }

        [JsonPropertyName("messages_unacknowledged")]
        public long MessagesUnacknowledged { get; set; }

        [JsonPropertyName("consumers")]
        public int Consumers { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }
    }
}
