namespace Courier.Branches.Domain;

public class Branch
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public BranchType Type { get; set; }

    // Normalized Nepal geography (ROADMAP Phase 3, CLARIFICATIONS.md #2). WardNumber is a plain
    // int scoped to LocalLevelId, not a FK to Ward.Id — Ward exists as its own entity so
    // Area/DeliveryZone coverage can FK into it, but a Branch's own address only needs to name
    // "ward number N of this local level", identical to MerchantAddress's field shape.
    public required Guid ProvinceId { get; set; }
    public required Guid DistrictId { get; set; }
    public required Guid LocalLevelId { get; set; }
    public required int WardNumber { get; set; }

    public required string AddressLine { get; set; }

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? Phone { get; set; }
    public string? OperatingHours { get; set; }
    public Guid? ParentBranchId { get; set; }
    public bool IsActive { get; set; } = true;
}
