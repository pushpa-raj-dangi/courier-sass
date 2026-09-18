using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Courier.Platform.Infrastructure.Configuration;

// First Redis pub/sub usage in this codebase (elsewhere Redis is only used via GetDatabase(),
// e.g. RedisRateLimiter). Because pub/sub has no delivery guarantee, a 5-minute reconciliation
// timer runs alongside the subscription as defense-in-depth, so a missed message self-heals
// instead of silently drifting.
public class PlatformSettingsChangeListener : BackgroundService
{
    public const string ChannelName = "platform-settings:changed";
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromMinutes(5);

    private readonly IConnectionMultiplexer _redis;
    private readonly IConfiguration _configuration;
    private readonly IPlatformSettingsCache _cache;
    private readonly ILogger<PlatformSettingsChangeListener> _logger;

    public PlatformSettingsChangeListener(IConnectionMultiplexer redis, IConfiguration configuration, IPlatformSettingsCache cache, ILogger<PlatformSettingsChangeListener> logger)
    {
        _redis = redis;
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PlatformSettingsReloader.ReloadAsync(_configuration, _cache, stoppingToken);

        var subscriber = _redis.GetSubscriber();
        await subscriber.SubscribeAsync(RedisChannel.Literal(ChannelName), async (_, _) =>
        {
            try
            {
                await PlatformSettingsReloader.ReloadAsync(_configuration, _cache, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload platform settings after a change notification.");
            }
        });

        using var timer = new PeriodicTimer(ReconciliationInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PlatformSettingsReloader.ReloadAsync(_configuration, _cache, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Periodic platform settings reconciliation failed.");
            }
        }
    }
}
