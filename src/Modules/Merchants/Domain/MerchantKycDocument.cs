namespace Courier.Merchants.Domain;

public class MerchantKycDocument
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }

    // Denormalized from Merchant.OrganizationId — same reasoning as MerchantAddress.OrganizationId.
    public Guid OrganizationId { get; set; }

    public KycDocumentType DocumentType { get; set; }
    public required string StorageKey { get; set; }
    public KycStatus Status { get; set; } = KycStatus.Pending;

    // No FK to a staff User: Merchants has no project reference to Identity. Resolved
    // server-side from the reviewing staff member's ClaimsPrincipal at review time, same
    // reasoning as Branch/DeliveryZone's acting-user ids.
    public Guid? ReviewedByUserId { get; set; }

    // Nullable: rows created before these columns existed have no recorded dates.
    public DateTime? UploadedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
}
