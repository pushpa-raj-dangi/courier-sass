using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Courier.International.Application.Carriers;
using Courier.International.Infrastructure.Carriers;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Courier.Api.Tests.International;

// Carrier-side state shared by the stub handler and the tests.
public sealed class FakeCarrierBackend
{
    private int _bookCalls;
    private int _nextAwb;

    public int BookCalls => _bookCalls;
    public HttpStatusCode? FailBookingWith { get; set; }
    public ConcurrentDictionary<string, string> AwbByIdempotencyKey { get; } = new();
    public ConcurrentDictionary<string, List<FakeTrackingEvent>> TrackingByAwb { get; } = new();
    public ConcurrentBag<string> Cancelled { get; } = new();

    public int RecordBookCall() => Interlocked.Increment(ref _bookCalls);
    public string NewAwb() => $"FAKE{Interlocked.Increment(ref _nextAwb):D6}{Random.Shared.Next(1000, 9999)}";
}

public sealed record FakeTrackingEvent(string CarrierAwb, string EventId, string StatusCode, string? Description, string? Location, DateTimeOffset OccurredAt);

// In-process stand-in for the carrier's HTTP API.
public sealed class FakeCarrierHttpHandler : HttpMessageHandler
{
    private readonly FakeCarrierBackend _backend;

    public FakeCarrierHttpHandler(FakeCarrierBackend backend) => _backend = backend;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.Trim('/');
        if (request.Method == HttpMethod.Post && path == "book")
        {
            _backend.RecordBookCall();
            if (_backend.FailBookingWith is HttpStatusCode failure)
            {
                return Task.FromResult(new HttpResponseMessage(failure) { Content = new StringContent("{\"error\":\"carrier says no\"}") });
            }
            var key = request.Headers.GetValues("Idempotency-Key").Single();
            var awb = _backend.AwbByIdempotencyKey.GetOrAdd(key, _ => _backend.NewAwb());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { carrierAwb = awb, serviceLevel = "EXPRESS", labelBase64 = Convert.ToBase64String("%PDF-fake-label"u8.ToArray()) })
            });
        }
        if (request.Method == HttpMethod.Post && path.StartsWith("cancel/"))
        {
            _backend.Cancelled.Add(path["cancel/".Length..]);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
        if (request.Method == HttpMethod.Get && path.StartsWith("tracking/"))
        {
            var events = _backend.TrackingByAwb.GetValueOrDefault(path["tracking/".Length..]) ?? new List<FakeTrackingEvent>();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(events) });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

public sealed class FakeHttpCarrierProvider : ICarrierProvider
{
    public const string Code = "FAKE_HTTP";
    public const string SignatureHeader = "X-Fake-Signature";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public FakeHttpCarrierProvider(HttpClient http)
    {
        _http = http;
        _http.BaseAddress ??= new Uri("http://fake-carrier.test/");
    }

    public string CarrierCode => Code;
    public CarrierCapabilities Capabilities { get; } = new(SupportsWebhooks: true, SupportsPolling: true, RequiresManualAwb: false, SupportsLabels: true);

    public static string Sign(byte[] body, string secret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    public async Task<CarrierBookingResult> BookAsync(CarrierBookingRequest request, CarrierCredentials credentials, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "book") { Content = JsonContent.Create(new { request.PlatformAwb, request.ServiceLevel, request.WeightKg }) };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await _http.SendAsync(message, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new CarrierBookingException($"Fake carrier returned {(int)response.StatusCode}.");
        }
        var body = JsonSerializer.Deserialize<BookBody>(raw, Json)!;
        return new CarrierBookingResult(body.CarrierAwb, body.ServiceLevel,
            body.LabelBase64 is null ? null : new CarrierLabel(Convert.FromBase64String(body.LabelBase64), "application/pdf"), raw);
    }

    public async Task CancelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync($"cancel/{carrierAwb}", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new CarrierBookingException($"Fake carrier returned {(int)response.StatusCode}.");
        }
    }

    public Task<CarrierLabel?> GetLabelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken) => Task.FromResult<CarrierLabel?>(null);

    public async Task<IReadOnlyList<CarrierTrackingUpdate>> GetTrackingAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken)
    {
        var events = await _http.GetFromJsonAsync<List<FakeTrackingEvent>>($"tracking/{carrierAwb}", Json, cancellationToken) ?? new();
        return events.Select(ToUpdate).ToList();
    }

    public bool VerifyWebhook(IReadOnlyDictionary<string, string> headers, byte[] rawBody, string webhookSecret)
    {
        if (!headers.TryGetValue(SignatureHeader, out var provided))
        {
            return false;
        }
        var expected = Encoding.ASCII.GetBytes(Sign(rawBody, webhookSecret));
        var actual = Encoding.ASCII.GetBytes(provided.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public IReadOnlyList<CarrierTrackingUpdate> ParseWebhook(byte[] rawBody)
    {
        try
        {
            return (JsonSerializer.Deserialize<List<FakeTrackingEvent>>(rawBody, Json) ?? new()).Select(ToUpdate).ToList();
        }
        catch (JsonException ex)
        {
            throw new CarrierWebhookPayloadException(ex.Message);
        }
    }

    public bool IsKnownStatusCode(string carrierStatusCode) => carrierStatusCode.ToUpperInvariant() is "TRANSIT" or "HOLD" or "CLEARANCE" or "DELIVERED";

    public ShipmentStatus? MapStatus(string carrierStatusCode, ShipmentDirection direction) => (carrierStatusCode.ToUpperInvariant(), direction) switch
    {
        ("TRANSIT", _) => ShipmentStatus.InInternationalTransit,
        ("HOLD", _) => ShipmentStatus.CustomsHold,
        ("CLEARANCE", ShipmentDirection.Inbound) => ShipmentStatus.CustomsClearance,
        ("DELIVERED", ShipmentDirection.Outbound) => ShipmentStatus.DeliveredAbroad,
        _ => null
    };

    private static CarrierTrackingUpdate ToUpdate(FakeTrackingEvent e) =>
        new(e.CarrierAwb, e.EventId, e.StatusCode, e.Description, e.Location, e.OccurredAt, JsonSerializer.Serialize(e, Json));

    private sealed record BookBody(string CarrierAwb, string? ServiceLevel, string? LabelBase64);
}

// One app instance per test class (booting per test would start a Hangfire server per test).
public sealed class FakeCarrierAppFixture : IDisposable
{
    public FakeCarrierAppFixture()
    {
        Backend = new FakeCarrierBackend();
        Factory = CreateFactory(Backend, o => o.RetryBaseDelay = TimeSpan.Zero);
    }

    public FakeCarrierBackend Backend { get; }
    public WebApplicationFactory<Program> Factory { get; }

    public static WebApplicationFactory<Program> CreateFactory(FakeCarrierBackend backend, Action<CarrierResilienceOptions> configure) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(backend);
            services.AddCarrierHttpClient<FakeHttpCarrierProvider, FakeHttpCarrierProvider>(configure)
                .ConfigurePrimaryHttpMessageHandler(sp => new FakeCarrierHttpHandler(sp.GetRequiredService<FakeCarrierBackend>()));
            services.AddScoped<ICarrierProvider>(sp => sp.GetRequiredService<FakeHttpCarrierProvider>());
        }));

    public void Dispose() => Factory.Dispose();
}
