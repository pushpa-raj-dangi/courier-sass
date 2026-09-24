using System.Net;
using Courier.International.Application.Carriers;
using Courier.International.Infrastructure.Carriers;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;

namespace Courier.International.Tests.Carriers;

public class CarrierHttpClientExtensionsTests
{
    public sealed class ProbeClient
    {
        private readonly HttpClient _http;
        public ProbeClient(HttpClient http) => _http = http;
        public Task<HttpResponseMessage> PingAsync() => _http.GetAsync("http://carrier.test/ping");
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _script;
        private readonly HttpStatusCode _fallback;
        public int Calls { get; private set; }

        public ScriptedHandler(HttpStatusCode fallback, params HttpStatusCode[] script)
        {
            _fallback = fallback;
            _script = new Queue<HttpStatusCode>(script);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(_script.Count > 0 ? _script.Dequeue() : _fallback));
        }
    }

    private static ProbeClient BuildClient(ScriptedHandler handler, Action<CarrierResilienceOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCarrierHttpClient<ProbeClient, ProbeClient>(configure).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<ProbeClient>();
    }

    [Fact]
    public async Task TransientServerError_IsRetried_ThenSucceeds()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, HttpStatusCode.InternalServerError);
        var client = BuildClient(handler, o => o.RetryBaseDelay = TimeSpan.Zero);

        var response = await client.PingAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ClientError_IsNotRetried()
    {
        var handler = new ScriptedHandler(HttpStatusCode.BadRequest);
        var client = BuildClient(handler, o => o.RetryBaseDelay = TimeSpan.Zero);

        var response = await client.PingAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RepeatedFailures_OpenTheCircuit_AndLaterCallsNeverReachTheCarrier()
    {
        var handler = new ScriptedHandler(HttpStatusCode.ServiceUnavailable);
        var client = BuildClient(handler, o =>
        {
            o.RetryBaseDelay = TimeSpan.Zero;
            o.MaxRetryAttempts = 1;
            o.MinimumThroughput = 2;
            o.SamplingDuration = TimeSpan.FromSeconds(30);
            o.BreakDuration = TimeSpan.FromSeconds(30);
        });

        BrokenCircuitException? broken = null;
        for (var i = 0; i < 5 && broken is null; i++)
        {
            try
            {
                await client.PingAsync();
            }
            catch (BrokenCircuitException ex)
            {
                broken = ex;
            }
        }

        Assert.NotNull(broken);
        Assert.True(CarrierCallFailures.IsCarrierUnavailable(broken));
        var callsWhenOpen = handler.Calls;
        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.PingAsync());
        Assert.Equal(callsWhenOpen, handler.Calls);
    }

    [Fact]
    public void Failures_AreClassified()
    {
        Assert.True(CarrierCallFailures.IsCarrierFailure(new HttpRequestException("down")));
        Assert.True(CarrierCallFailures.IsCarrierFailure(new CarrierBookingException("rejected")));
        Assert.True(CarrierCallFailures.IsCarrierFailure(new TaskCanceledException("timeout")));
        Assert.False(CarrierCallFailures.IsCarrierFailure(new InvalidOperationException("bug")));
        Assert.False(CarrierCallFailures.IsCarrierUnavailable(new HttpRequestException("down")));
    }
}
