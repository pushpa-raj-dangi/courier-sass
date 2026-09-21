using System.Text.Json;
using Courier.Audit.Application;
using Courier.Pricing.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Pricing.Application;

public sealed record FxRateDto(Guid Id, string FromCurrency, string ToCurrency, decimal Rate, DateTimeOffset EffectiveAt, string Source, Guid? CreatedByUserId, DateTimeOffset CreatedAt);

public sealed record RecordFxRateRequest(string FromCurrency, string ToCurrency, decimal Rate, DateTimeOffset? EffectiveAt);

// FxRateId is null only for a same-currency "conversion" (identity), where no rate is involved.
public sealed record FxConversion(Money Source, Money Converted, Guid? FxRateId, decimal Rate);

public class FxRateValidationException : Exception
{
    public FxRateValidationException(string message) : base(message) { }
}

public class FxRateNotFoundException : Exception
{
    public FxRateNotFoundException(CurrencyCode from, CurrencyCode to)
        : base($"No FX rate is available for {from} to {to}.") { }
}

// Consumed by other modules (international pricing) so they never touch the FxRates table.
public interface IFxConversionService
{
    Task<FxConversion> ConvertAsync(Money source, CurrencyCode target, DateTimeOffset? asOf = null, CancellationToken cancellationToken = default);
}

public class FxRateService : IFxConversionService
{
    // Absorbs clock skew between an operator's browser/server and this host; anything further
    // ahead is almost certainly a typo, and a future-dated rate would be silently ignored by
    // GetLatestAsync until then, which is a confusing failure mode.
    private static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(5);
    private const string ManualSource = "manual";

    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;

    public FxRateService(DbContext dbContext, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
    }

    public async Task<FxRateDto> RecordAsync(RecordFxRateRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        if (!CurrencyCode.TryParse(request.FromCurrency, out var from))
        {
            throw new FxRateValidationException("From currency must be a three-letter currency code.");
        }
        if (!CurrencyCode.TryParse(request.ToCurrency, out var to))
        {
            throw new FxRateValidationException("To currency must be a three-letter currency code.");
        }
        if (from == to)
        {
            throw new FxRateValidationException("From and to currencies must differ.");
        }
        if (request.Rate <= 0m)
        {
            throw new FxRateValidationException("Rate must be greater than zero.");
        }
        // Rate is stored as numeric(18,8): round here so entity, DTO, audit and DB all agree, and
        // reject values the column cannot hold (rounds to zero, or overflows 10 integer digits).
        var normalizedRate = Math.Round(request.Rate, 8, MidpointRounding.AwayFromZero);
        if (normalizedRate <= 0m || normalizedRate >= 10_000_000_000m)
        {
            throw new FxRateValidationException("Rate must be at least 0.00000001 and less than 10,000,000,000.");
        }

        var now = DateTimeOffset.UtcNow;
        var effectiveAt = (request.EffectiveAt ?? now).ToUniversalTime();
        if (effectiveAt > now + MaxFutureSkew)
        {
            throw new FxRateValidationException("Effective time cannot be in the future.");
        }

        var rate = new FxRate
        {
            Id = Guid.NewGuid(),
            FromCurrency = from.Value,
            ToCurrency = to.Value,
            Rate = normalizedRate,
            EffectiveAt = effectiveAt,
            Source = ManualSource,
            CreatedByUserId = actingUserId,
            CreatedAt = now,
        };
        _dbContext.Set<FxRate>().Add(rate);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = ToDto(rate);
        await _auditLogger.LogAsync(new AuditEntry(
            null, actingUserId, "fx_rate.recorded", "FxRate", rate.Id.ToString(),
            Before: null, After: JsonSerializer.Serialize(dto), IsCrossTenantAction: true), cancellationToken);

        return dto;
    }

    public async Task<FxRate?> GetLatestAsync(CurrencyCode from, CurrencyCode to, DateTimeOffset? asOf, CancellationToken cancellationToken = default)
    {
        var instant = (asOf ?? DateTimeOffset.UtcNow).ToUniversalTime();
        return await _dbContext.Set<FxRate>()
            .AsNoTracking()
            .Where(r => r.FromCurrency == from.Value && r.ToCurrency == to.Value && r.EffectiveAt <= instant)
            .OrderByDescending(r => r.EffectiveAt)
            .ThenByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FxConversion> ConvertAsync(Money source, CurrencyCode target, DateTimeOffset? asOf = null, CancellationToken cancellationToken = default)
    {
        if (source.Currency == target)
        {
            return new FxConversion(source, source, null, 1m);
        }

        var rate = await GetLatestAsync(source.Currency, target, asOf, cancellationToken)
            ?? throw new FxRateNotFoundException(source.Currency, target);
        return new FxConversion(source, source.ConvertTo(target, rate.Rate), rate.Id, rate.Rate);
    }

    public async Task<PagedResult<FxRateDto>> ListAsync(string? from, string? to, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var query = _dbContext.Set<FxRate>().AsNoTracking();
        if (CurrencyCode.TryParse(from, out var fromCode))
        {
            query = query.Where(r => r.FromCurrency == fromCode.Value);
        }
        if (CurrencyCode.TryParse(to, out var toCode))
        {
            query = query.Where(r => r.ToCurrency == toCode.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(r => r.EffectiveAt)
            .ThenByDescending(r => r.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<FxRateDto>(rows.Select(ToDto).ToList(), normalizedPage, normalizedPageSize, total);
    }

    private static FxRateDto ToDto(FxRate r) =>
        new(r.Id, r.FromCurrency, r.ToCurrency, r.Rate, r.EffectiveAt, r.Source, r.CreatedByUserId, r.CreatedAt);
}
