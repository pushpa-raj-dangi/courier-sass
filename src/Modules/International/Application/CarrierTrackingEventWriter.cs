using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Courier.International.Application;

// The single insert path for carrier tracking events from every source (webhook, poll, manual).
// Deduplicates by (CarrierAccountId, CarrierEventId) — within the batch, against stored rows, and
// against a concurrent insert racing on the unique index.
public class CarrierTrackingEventWriter
{
    private const int MaxAwbLength = 100;
    private const int MaxEventIdLength = 200;
    private const int MaxStatusCodeLength = 100;
    private const int MaxDescriptionLength = 1000;
    private const int MaxLocationLength = 300;

    private readonly DbContext _dbContext;
    private readonly ILogger<CarrierTrackingEventWriter> _logger;

    public CarrierTrackingEventWriter(DbContext dbContext, ILogger<CarrierTrackingEventWriter> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<int> InsertAsync(CarrierAccount account, IReadOnlyList<CarrierTrackingUpdate> updates, CarrierEventSource source,
        Guid? enteredByUserId, IEnumerable<string> secretValues, CancellationToken cancellationToken)
    {
        var storable = new List<CarrierTrackingUpdate>();
        foreach (var update in updates)
        {
            if (IsStorable(update))
            {
                storable.Add(update);
            }
            else
            {
                _logger.LogWarning("Skipped an unstorable carrier tracking update for carrier account {CarrierAccountId}.", account.Id);
            }
        }
        var distinct = storable.GroupBy(u => u.CarrierEventId).Select(g => g.First()).ToList();
        if (distinct.Count == 0)
        {
            return 0;
        }

        // Deliberate IgnoreQueryFilters: the webhook path has no tenant context. The account was
        // already resolved and every row here is scoped to it by CarrierAccountId.
        var eventIds = distinct.Select(u => u.CarrierEventId).ToList();
        var existing = (await _dbContext.Set<CarrierTrackingEvent>().IgnoreQueryFilters()
            .Where(e => e.CarrierAccountId == account.Id && eventIds.Contains(e.CarrierEventId))
            .Select(e => e.CarrierEventId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var secrets = secretValues.ToList();
        var now = DateTimeOffset.UtcNow;
        var toInsert = distinct.Where(u => !existing.Contains(u.CarrierEventId)).Select(u => new CarrierTrackingEvent
        {
            Id = Guid.NewGuid(),
            OrganizationId = account.OrganizationId,
            CarrierAccountId = account.Id,
            CarrierAwb = u.CarrierAwb.Trim(),
            CarrierEventId = u.CarrierEventId,
            CarrierStatusCode = u.CarrierStatusCode.Trim(),
            Description = Truncate(u.Description, MaxDescriptionLength),
            Location = Truncate(u.Location, MaxLocationLength),
            OccurredAt = u.OccurredAt,
            Source = source,
            RawPayload = CarrierPayloadSanitizer.ToStoredJson(u.RawPayload, secrets),
            EnteredByUserId = enteredByUserId,
            ReceivedAt = now,
            ProcessingStatus = CarrierEventProcessingStatus.Pending
        }).ToList();
        if (toInsert.Count == 0)
        {
            return 0;
        }

        _dbContext.Set<CarrierTrackingEvent>().AddRange(toInsert);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return toInsert.Count;
        }
        // Only a genuine unique-index race (the (CarrierAccountId, CarrierEventId) index) falls
        // back to a one-row-at-a-time insert; anything else (a real data/constraint problem)
        // rethrows instead of being silently swallowed batch-wide.
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Another source inserted one of these first — fall back to one row at a time.
            foreach (var row in toInsert)
            {
                _dbContext.Entry(row).State = EntityState.Detached;
            }
            var inserted = 0;
            foreach (var row in toInsert)
            {
                _dbContext.Set<CarrierTrackingEvent>().Add(row);
                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    inserted++;
                }
                catch (DbUpdateException rowEx) when (IsUniqueViolation(rowEx))
                {
                    // Expected: this row really was a duplicate — silently drop it.
                    _dbContext.Entry(row).State = EntityState.Detached;
                }
                catch (DbUpdateException rowEx)
                {
                    // Unexpected: something other than a duplicate stopped this one row — drop it
                    // (the batch already made progress on the others) but never silently.
                    _dbContext.Entry(row).State = EntityState.Detached;
                    _logger.LogError(rowEx, "Dropped carrier tracking event {CarrierEventId} for carrier account {CarrierAccountId}: {ExceptionType}.",
                        row.CarrierEventId, account.Id, rowEx.GetType().Name);
                }
            }
            return inserted;
        }
    }

    // Postgres' unique_violation SqlState (23505) — the only DbUpdateException case the
    // (CarrierAccountId, CarrierEventId) unique index can legitimately race on. Compared by
    // SqlState (not just the exception type) so this only matches that specific constraint
    // failure, not any other PostgresException a save could throw.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException postgresException && postgresException.SqlState == PostgresErrorCodes.UniqueViolation;

    private static bool IsStorable(CarrierTrackingUpdate update) =>
        !string.IsNullOrWhiteSpace(update.CarrierAwb) && update.CarrierAwb.Trim().Length <= MaxAwbLength
        && !string.IsNullOrWhiteSpace(update.CarrierEventId) && update.CarrierEventId.Length <= MaxEventIdLength
        && !string.IsNullOrWhiteSpace(update.CarrierStatusCode) && update.CarrierStatusCode.Trim().Length <= MaxStatusCodeLength;

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null : value.Length <= maxLength ? value : value[..maxLength];
}
