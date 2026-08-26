using System.Text;
using Courier.Events.Application;
using RabbitMQ.Client;

namespace Courier.Events.Infrastructure;

public class RabbitMqEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly IConnection _connection;
    private IChannel? _channel;
    private readonly SemaphoreSlim _channelLock = new(1, 1);

    public RabbitMqEventPublisher(IConnection connection)
    {
        _connection = connection;
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await _channelLock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            // Declaring on every publish is idempotent and cheap — this keeps the publisher
            // correct even if it starts before any consumer has ever declared the exchange.
            await _channel.ExchangeDeclareAsync(Courier.SharedKernel.RabbitMqTopology.DomainEventsExchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
            return _channel;
        }
        finally
        {
            _channelLock.Release();
        }
    }

    public async Task PublishAsync(string exchange, string routingKey, string payload, CancellationToken cancellationToken = default)
    {
        var channel = await GetChannelAsync(cancellationToken);
        var body = Encoding.UTF8.GetBytes(payload);
        await channel.BasicPublishAsync(exchange, routingKey, body: body, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
        }
        _channelLock.Dispose();
    }
}
