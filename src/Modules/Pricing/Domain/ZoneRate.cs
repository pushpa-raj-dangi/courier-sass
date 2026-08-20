namespace Courier.Pricing.Domain;

// OriginZoneId/DestinationZoneId FK into Geography.DeliveryZone directly — Pricing has no
// separate Zone entity (see this plan's Architecture section, deviation 2). No OrganizationId of
// its own: tenant isolation flows transitively through RateCardId -> RateCard.OrganizationId, so
// every query here must be scoped through a RateCard the caller's tenant actually owns.
public class ZoneRate
{
    public Guid Id { get; set; }
    public Guid RateCardId { get; set; }
    public Guid OriginZoneId { get; set; }
    public Guid DestinationZoneId { get; set; }
    public Guid ServiceTypeId { get; set; }
    public Guid WeightSlabId { get; set; }
    public decimal BaseRate { get; set; }
}
