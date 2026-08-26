using System.Text;
using System.Text.Json;
using Courier.Webhooks.Application;
using Courier.Webhooks.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Courier.Webhooks.Infrastructure;

// Binds its own queue (RabbitMqTopology.WebhooksQueue) to the shared exchange, independently of
// NotificationsConsumer — same shared exchange, two separate queues, so both consumers see
// every published event without competing for the same messages (a topic exchange fans out to
// every bound queue whose binding matches, it does not load-balance across them).
public class WebhooksConsumer : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhooksConsumer> _logger;
    private IChannel? _channel;

    public WebhooksConsumer(IConnection connection, IServiceScopeFactory scopeFactory, ILogger<WebhooksConsumer> logger)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await _channel.ExchangeDeclareAsync(RabbitMqTopology.DomainEventsExchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync(RabbitMqTopology.WebhooksQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(RabbitMqTopology.WebhooksQueue, RabbitMqTopology.DomainEventsExchange, routingKey: "#", cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            var eventType = eventArgs.RoutingKey;
            var rawEnvelope = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
            try
            {
                var envelope = JsonDocument.Parse(rawEnvelope).RootElement;
                if (!envelope.TryGetProperty("EventId", out var eventIdProperty) || !eventIdProperty.TryGetGuid(out var sourceEventId))
                {
                    throw new WebhookValidationException("Envelope has no EventId.");
                }
                if (!envelope.TryGetProperty("OrganizationId", out var orgProperty) || !orgProperty.TryGetGuid(out var organizationId))
                {
                    throw new WebhookValidationException("Envelope has no OrganizationId.");
                }
                if (!envelope.TryGetProperty("Data", out var dataElement))
                {
                    throw new WebhookValidationException("Envelope has no Data.");
                }
                var payloadJson = dataElement.GetRawText();

                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
                var attemptService = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptService>();

                var matchingSubscriptions = await dbContext.Set<WebhookSubscription>().IgnoreQueryFilters()
                    .Where(s => s.OrganizationId == organizationId && s.IsActive && s.Events.Contains(eventType))
                    .Select(s => s.Id)
                    .ToListAsync(stoppingToken);

                foreach (var subscriptionId in matchingSubscriptions)
                {
                    var alreadyExists = await dbContext.Set<WebhookDelivery>()
                        .AnyAsync(d => d.WebhookSubscriptionId == subscriptionId && d.SourceEventId == sourceEventId, stoppingToken);
                    if (alreadyExists)
                    {
                        continue;
                    }

                    var delivery = new WebhookDelivery
                    {
                        Id = Guid.NewGuid(),
                        WebhookSubscriptionId = subscriptionId,
                        SourceEventId = sourceEventId,
                        EventType = eventType,
                        Payload = payloadJson,
                        Attempt = 0
                    };
                    dbContext.Set<WebhookDelivery>().Add(delivery);
                    try
                    {
                        await dbContext.SaveChangesAsync(stoppingToken);
                    }
                    catch (DbUpdateException)
                    {
                        // The unique (WebhookSubscriptionId, SourceEventId) index (Task 9) caught
                        // a redelivered message that raced past the AnyAsync pre-check above —
                        // this exact source event already has a delivery row for this
                        // subscription. Detach and move on; nothing to send twice.
                        dbContext.Entry(delivery).State = EntityState.Detached;
                        continue;
                    }

                    await attemptService.AttemptAsync(delivery.Id, stoppingToken);
                }

                await _channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process webhook fan-out for event {EventType}", eventType);
                await _channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
            }
        };

        await _channel.BasicConsumeAsync(RabbitMqTopology.WebhooksQueue, autoAck: false, consumer, cancellationToken: stoppingToken);
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
