namespace Courier.Billing.Domain;

// Platform-global: no OrganizationId. Only a SuperAdmin can create/retire a Plan;
// any organization can read its own assigned Plan's limits via IBillingLookupService.
public class Plan
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public decimal MonthlyPriceNpr { get; set; }
    public int? MaxBranches { get; set; }
    public int? MaxStaffUsers { get; set; }
    public int? MaxMonthlyShipments { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
