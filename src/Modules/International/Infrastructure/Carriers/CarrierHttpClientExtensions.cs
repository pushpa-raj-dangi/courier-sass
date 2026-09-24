using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Courier.International.Infrastructure.Carriers;

// Defaults are the I4 spec §5.9 values; tests shrink them.
public sealed class CarrierResilienceOptions
{
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);
    public double FailureRatio { get; set; } = 0.5;
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);
    public int MinimumThroughput { get; set; } = 10;
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}

public static class CarrierHttpClientExtensions
{
    public const string PipelineName = "carrier";

    // Registers a typed HttpClient for a carrier adapter with the standard carrier pipeline:
    // total timeout -> retry (5xx/408/429/HttpRequestException/timeouts, exponential + jitter) ->
    // circuit breaker -> per-attempt timeout. Booking calls are safe to retry only because
    // adapters send our idempotency key to the carrier.
    public static IHttpClientBuilder AddCarrierHttpClient<TClient, TImplementation>(this IServiceCollection services, Action<CarrierResilienceOptions>? configure = null)
        where TClient : class
        where TImplementation : class, TClient
    {
        var options = new CarrierResilienceOptions();
        configure?.Invoke(options);

        var builder = services.AddHttpClient<TClient, TImplementation>();
        builder.AddResilienceHandler(PipelineName, pipeline =>
        {
            pipeline.AddTimeout(options.TotalTimeout);
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.RetryBaseDelay
            });
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = options.FailureRatio,
                SamplingDuration = options.SamplingDuration,
                MinimumThroughput = options.MinimumThroughput,
                BreakDuration = options.BreakDuration
            });
            pipeline.AddTimeout(options.AttemptTimeout);
        });
        return builder;
    }
}
