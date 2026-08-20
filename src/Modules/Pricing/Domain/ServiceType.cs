namespace Courier.Pricing.Domain;

// Shared/global reference data (like Geography's Province/District) — no OrganizationId, seeded
// once, no create/update endpoint this phase.
public class ServiceType
{
    public Guid Id { get; set; }
    public ServiceTypeCode Code { get; set; }
    public required string Name { get; set; }
}
