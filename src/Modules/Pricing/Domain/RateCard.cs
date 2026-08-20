namespace Courier.Pricing.Domain;

// Immutable once created (CLAUDE.md §16): a rate change creates a new RateCard row with
// Version = previous.Version + 1 and flips the previous row's IsActive to false. At most one
// RateCard per OrganizationId has IsActive == true at a time (enforced in RateCardService, not a
// DB constraint — see this plan's Architecture section).
public class RateCard
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public int Version { get; set; }
    public bool IsActive { get; set; }
}
