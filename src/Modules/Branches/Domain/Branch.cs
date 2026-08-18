namespace Courier.Branches.Domain;

public class Branch
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public BranchType Type { get; set; }

    // Free-text placeholder for Nepal geography. ROADMAP Phase 3 replaces this with
    // ProvinceId/DistrictId/LocalLevelId/WardNumber FKs into the Geography module, which does
    // not exist yet (CLARIFICATIONS.md #2).
    public required string AddressLine { get; set; }

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? Phone { get; set; }
    public string? OperatingHours { get; set; }
    public Guid? ParentBranchId { get; set; }
    public bool IsActive { get; set; } = true;
}
