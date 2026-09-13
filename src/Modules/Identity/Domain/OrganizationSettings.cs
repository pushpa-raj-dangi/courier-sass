namespace Courier.Identity.Domain;

// One row per organization, created lazily on first read or write (OrganizationSettingsService
// get-or-creates — no backfill migration, no "row missing" error path anywhere else in the
// codebase needs to exist).
public class OrganizationSettings
{
    public Guid OrganizationId { get; set; }

    // Profile
    public string? SupportEmail { get; set; }
    public string? SupportPhone { get; set; }
    public string? AddressLine { get; set; }
    public string? LogoUrl { get; set; }
    public string TimeZoneId { get; set; } = "Asia/Kathmandu";
    // Free text, mirrors Branch.OperatingHours — no structured-hours type exists in this codebase.
    public string? BusinessHours { get; set; }
    public DateTimeOffset? ProfileUpdatedAt { get; set; }
    public Guid? ProfileUpdatedByUserId { get; set; }

    // Security policy
    public int RefreshTokenLifetimeDays { get; set; } = 7;
    public int MinPasswordLength { get; set; } = 8;
    public DateTimeOffset? SecurityUpdatedAt { get; set; }
    public Guid? SecurityUpdatedByUserId { get; set; }

    // Operational defaults
    public Guid? DefaultServiceTypeId { get; set; }
    public Guid? DefaultOriginBranchId { get; set; }
    public DateTimeOffset? DefaultsUpdatedAt { get; set; }
    public Guid? DefaultsUpdatedByUserId { get; set; }
}
