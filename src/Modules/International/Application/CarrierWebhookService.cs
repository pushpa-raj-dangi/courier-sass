using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courier.International.Application;

public enum CarrierWebhookOutcome
{
    Accepted,
    NotFound,
    InvalidSignature,
    InvalidPayload
}

// Anonymous ingest: the webhook is authenticated only by the per-account signature, and the
// tenant comes only from the resolved account — never from the payload (I4 spec §5.5, §8).
public class CarrierWebhookService
{
    private readonly DbContext _dbContext;
    private readonly ICarrierProviderRegistry _registry;
    private readonly ICarrierSecretProtector _secretProtector;
    private readonly CarrierTrackingEventWriter _writer;
    private readonly ICarrierJobScheduler _scheduler;
    private readonly ILogger<CarrierWebhookService> _logger;

    public CarrierWebhookService(DbContext dbContext, ICarrierProviderRegistry registry, ICarrierSecretProtector secretProtector,
        CarrierTrackingEventWriter writer, ICarrierJobScheduler scheduler, ILogger<CarrierWebhookService> logger)
    {
        _dbContext = dbContext;
        _registry = registry;
        _secretProtector = secretProtector;
        _writer = writer;
        _scheduler = scheduler;
        _logger = logger;
    }

    public async Task<CarrierWebhookOutcome> ReceiveAsync(string webhookPublicId, IReadOnlyDictionary<string, string> headers, byte[] body, CancellationToken cancellationToken)
    {
        // Deliberate IgnoreQueryFilters: an anonymous request has no tenant; this single lookup by
        // the unguessable, platform-unique public id is what establishes it.
        var account = await _dbContext.Set<CarrierAccount>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.WebhookPublicId == webhookPublicId, cancellationToken);
        if (account is null || !account.IsActive || account.EncryptedWebhookSecret is null)
        {
            return CarrierWebhookOutcome.NotFound;
        }
        var provider = _registry.Find(account.CarrierCode);
        if (provider is null || !provider.Capabilities.SupportsWebhooks)
        {
            return CarrierWebhookOutcome.NotFound;
        }

        var webhookSecret = _secretProtector.UnprotectWebhookSecret(account.EncryptedWebhookSecret);
        if (!provider.VerifyWebhook(headers, body, webhookSecret))
        {
            _logger.LogWarning("Rejected a carrier webhook with an invalid signature for carrier account {CarrierAccountId}.", account.Id);
            return CarrierWebhookOutcome.InvalidSignature;
        }

        IReadOnlyList<CarrierTrackingUpdate> updates;
        try
        {
            updates = provider.ParseWebhook(body);
        }
        catch (CarrierWebhookPayloadException)
        {
            _logger.LogWarning("Rejected an unparseable carrier webhook for carrier account {CarrierAccountId}.", account.Id);
            return CarrierWebhookOutcome.InvalidPayload;
        }

        var credentials = _secretProtector.UnprotectCredentials(account.EncryptedCredentials);
        var inserted = await _writer.InsertAsync(account, updates, CarrierEventSource.Webhook, null,
            credentials.SecretValues.Append(webhookSecret), cancellationToken);
        if (inserted > 0)
        {
            _scheduler.EnqueueProcessing();
        }
        return CarrierWebhookOutcome.Accepted;
    }
}
