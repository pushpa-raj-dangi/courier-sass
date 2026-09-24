using System.Text.Json;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.International.Infrastructure;
using Courier.SharedKernel;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Tests;

internal sealed class CarrierTestDbContext : DbContext
{
    public CarrierTestDbContext(DbContextOptions<CarrierTestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new InternationalLegConfiguration());
        modelBuilder.ApplyConfiguration(new CustomsItemConfiguration());
        modelBuilder.ApplyConfiguration(new CarrierAccountConfiguration());
        modelBuilder.ApplyConfiguration(new CarrierBookingConfiguration());
        modelBuilder.ApplyConfiguration(new CarrierTrackingEventConfiguration());
    }
}

internal static class CarrierTestData
{
    public static CarrierTestDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<CarrierTestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new CarrierTestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    public static async Task<CarrierAccount> SeedAccountAsync(DbContext dbContext, Guid organizationId, string carrierCode,
        bool isActive = true, bool pollingEnabled = false, string? encryptedWebhookSecret = null, string? encryptedCredentials = null)
    {
        var account = new CarrierAccount
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, CarrierCode = carrierCode, DisplayName = carrierCode,
            IsActive = isActive, PollingEnabled = pollingEnabled, EncryptedWebhookSecret = encryptedWebhookSecret,
            EncryptedCredentials = encryptedCredentials, WebhookPublicId = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Set<CarrierAccount>().Add(account);
        await dbContext.SaveChangesAsync();
        return account;
    }

    public static async Task<InternationalLeg> SeedLegAsync(DbContext dbContext, Guid organizationId, Guid shipmentId)
    {
        var leg = new InternationalLeg
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = shipmentId, ProvisionalTotalChargeCurrency = "NPR",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Set<InternationalLeg>().Add(leg);
        await dbContext.SaveChangesAsync();
        return leg;
    }

    public static async Task<CarrierBooking> SeedBookingAsync(DbContext dbContext, Guid organizationId, Guid legId, Guid accountId,
        CarrierBookingStatus status, string? carrierAwb, CarrierBookingDirection direction = CarrierBookingDirection.Outbound)
    {
        var booking = new CarrierBooking
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, InternationalLegId = legId, CarrierAccountId = accountId,
            Direction = direction, Status = status, CarrierAwb = carrierAwb, IdempotencyKey = Guid.NewGuid().ToString("N"),
            BookedAt = status == CarrierBookingStatus.Booked ? DateTimeOffset.UtcNow : null,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Set<CarrierBooking>().Add(booking);
        await dbContext.SaveChangesAsync();
        return booking;
    }

    public static async Task<CarrierTrackingEvent> SeedEventAsync(DbContext dbContext, Guid organizationId, Guid accountId, string carrierAwb,
        string statusCode, DateTimeOffset occurredAt, CarrierEventProcessingStatus processingStatus = CarrierEventProcessingStatus.Processing)
    {
        var evt = new CarrierTrackingEvent
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, CarrierAccountId = accountId, CarrierAwb = carrierAwb,
            CarrierEventId = $"evt-{Guid.NewGuid():N}", CarrierStatusCode = statusCode, OccurredAt = occurredAt,
            Source = CarrierEventSource.Webhook, ReceivedAt = DateTimeOffset.UtcNow, ProcessingStatus = processingStatus
        };
        dbContext.Set<CarrierTrackingEvent>().Add(evt);
        await dbContext.SaveChangesAsync();
        return evt;
    }
}

// Configurable in-memory carrier for unit tests. Webhook "signature" = the X-Test-Signature
// header equal to the secret; webhook body = a JSON array of CarrierTrackingUpdate (web casing).
internal sealed class TestCarrierProvider : ICarrierProvider
{
    public const string SignatureHeader = "X-Test-Signature";

    private static readonly Dictionary<string, (ShipmentStatus? Outbound, ShipmentStatus? Inbound)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TRANSIT"] = (ShipmentStatus.InInternationalTransit, ShipmentStatus.InInternationalTransit),
        ["HOLD"] = (ShipmentStatus.CustomsHold, ShipmentStatus.CustomsHold),
        ["CLEARANCE"] = (null, ShipmentStatus.CustomsClearance),
        ["DELIVERED"] = (ShipmentStatus.DeliveredAbroad, null),
        ["INFO"] = (null, null)
    };

    public TestCarrierProvider(string code = "TESTCARRIER", bool webhooks = true, bool polling = true, bool manualAwb = false)
    {
        CarrierCode = code;
        Capabilities = new CarrierCapabilities(webhooks, polling, manualAwb, SupportsLabels: true);
    }

    public string CarrierCode { get; }
    public CarrierCapabilities Capabilities { get; }
    public Func<CarrierBookingRequest, CarrierBookingResult> Book { get; set; } =
        request => new CarrierBookingResult($"TC-{request.PlatformAwb}", request.ServiceLevel, null, "{\"ok\":true}");
    public Exception? BookException { get; set; }
    public int BookCalls { get; private set; }
    public List<string> CancelledAwbs { get; } = new();
    public List<CarrierTrackingUpdate> TrackingUpdates { get; } = new();
    public Exception? TrackingException { get; set; }

    public Task<CarrierBookingResult> BookAsync(CarrierBookingRequest request, CarrierCredentials credentials, string idempotencyKey, CancellationToken cancellationToken)
    {
        BookCalls++;
        if (BookException is not null)
        {
            throw BookException;
        }
        return Task.FromResult(Book(request));
    }

    public Task CancelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken)
    {
        CancelledAwbs.Add(carrierAwb);
        return Task.CompletedTask;
    }

    public Task<CarrierLabel?> GetLabelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken) => Task.FromResult<CarrierLabel?>(null);

    public Task<IReadOnlyList<CarrierTrackingUpdate>> GetTrackingAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken)
    {
        if (TrackingException is not null)
        {
            throw TrackingException;
        }
        return Task.FromResult<IReadOnlyList<CarrierTrackingUpdate>>(TrackingUpdates.Where(u => u.CarrierAwb == carrierAwb).ToList());
    }

    public bool VerifyWebhook(IReadOnlyDictionary<string, string> headers, byte[] rawBody, string webhookSecret) =>
        headers.TryGetValue(SignatureHeader, out var signature) && signature == webhookSecret;

    public IReadOnlyList<CarrierTrackingUpdate> ParseWebhook(byte[] rawBody)
    {
        try
        {
            return JsonSerializer.Deserialize<List<CarrierTrackingUpdate>>(rawBody, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new CarrierWebhookPayloadException("Empty webhook body.");
        }
        catch (JsonException ex)
        {
            throw new CarrierWebhookPayloadException(ex.Message);
        }
    }

    public bool IsKnownStatusCode(string carrierStatusCode) => Map.ContainsKey(carrierStatusCode);

    public ShipmentStatus? MapStatus(string carrierStatusCode, ShipmentDirection direction) =>
        Map.TryGetValue(carrierStatusCode, out var targets) ? (direction == ShipmentDirection.Outbound ? targets.Outbound : targets.Inbound) : null;
}

internal sealed class RecordingJobScheduler : ICarrierJobScheduler
{
    public int ProcessingEnqueued { get; private set; }
    public List<(Guid OrganizationId, Guid BookingId)> Polls { get; } = new();
    public void EnqueueProcessing() => ProcessingEnqueued++;
    public void EnqueuePoll(Guid organizationId, Guid bookingId) => Polls.Add((organizationId, bookingId));
}

internal sealed class InMemoryObjectStorage : IObjectStorage
{
    public Dictionary<string, (byte[] Content, string ContentType)> Objects { get; } = new();

    public async Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var key = $"{keyPrefix}/{Guid.NewGuid():N}";
        Objects[key] = (buffer.ToArray(), contentType);
        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new MemoryStream(Objects[key].Content));
}
