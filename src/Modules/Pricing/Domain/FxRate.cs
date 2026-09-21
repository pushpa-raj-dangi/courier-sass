namespace Courier.Pricing.Domain;

// Platform-level reference data: NOT tenant-owned, so it has no OrganizationId and no query
// filter. Append-only — a correction is a new row with a later EffectiveAt, never an edit, so
// every conversion that recorded an FxRate.Id stays reproducible. "Current" for a pair is the
// row with the greatest EffectiveAt <= the as-of instant (ties broken by CreatedAt).
// Rate means: 1 unit of FromCurrency = Rate units of ToCurrency.
public class FxRate
{
    public Guid Id { get; set; }
    public string FromCurrency { get; set; } = default!;
    public string ToCurrency { get; set; } = default!;
    public decimal Rate { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public string Source { get; set; } = default!;
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
