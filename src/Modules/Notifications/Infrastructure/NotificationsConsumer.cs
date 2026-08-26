using System.Text;
using System.Text.Json;
using Courier.Notifications.Application;
using Courier.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Courier.Notifications.Infrastructure;

// Binds its own queue (RabbitMqTopology.NotificationsQueue) to the shared domain-events topic
// exchange with a wildcard routing key ("#" — every event type), independently of the Webhooks
// consumer (Task 11), which binds its own separate queue to the same exchange. Each message is
// only ack'd after NotificationDispatchService.DispatchEventAsync completes without throwing —
// an unhandled exception nacks with requeue=true, matching "consumers acknowledge only after
// successfully completing their work" (Global Constraints). DispatchEventAsync itself never
// throws for expected conditions (no template, no recipient) — only for a malformed payload
// (NotificationValidationException) or an infrastructure fault, both of which are legitimately
// worth a requeue-and-retry rather than a silent drop.
public class NotificationsConsumer : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationsConsumer> _logger;
    private IChannel? _channel;

    public NotificationsConsumer(IConnection connection, IServiceScopeFactory scopeFactory, ILogger<NotificationsConsumer> logger)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await _channel.ExchangeDeclareAsync(RabbitMqTopology.DomainEventsExchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync(RabbitMqTopology.NotificationsQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(RabbitMqTopology.NotificationsQueue, RabbitMqTopology.DomainEventsExchange, routingKey: "#", cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            var eventType = eventArgs.RoutingKey;
            var rawEnvelope = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
            try
            {
                // Envelope shape published by OutboxPublisherJob (Task 4, amended by this task):
                // { EventId, OrganizationId, EventType, Data }. This consumer only needs
                // OrganizationId (to scope the dispatch) and Data (the original per-event payload
                // DispatchEventAsync expects) — EventId is Webhooks' (Task 11) idempotency key,
                // unused here.
                var envelope = JsonDocument.Parse(rawEnvelope).RootElement;
                if (!envelope.TryGetProperty("OrganizationId", out var orgProperty) || !orgProperty.TryGetGuid(out var organizationId))
                {
                    throw new NotificationValidationException("Envelope has no OrganizationId.");
                }
                if (!envelope.TryGetProperty("Data", out var dataElement))
                {
                    throw new NotificationValidationException("Envelope has no Data.");
                }

                using var scope = _scopeFactory.CreateScope();
                var dispatchService = scope.ServiceProvider.GetRequiredService<NotificationDispatchService>();
                await dispatchService.DispatchEventAsync(organizationId, eventType, dataElement.GetRawText(), stoppingToken);
                await _channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process notification for event {EventType}", eventType);
                await _channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
            }
        };

        await _channel.BasicConsumeAsync(RabbitMqTopology.NotificationsQueue, autoAck: false, consumer, cancellationToken: stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
        }
        await base.StopAsync(cancellationToken);
    }
}
