using Courier.Merchants.Domain;

namespace Courier.Merchants.Application;

// Staff (review endpoint) response.
public record MerchantKycDocumentResponse(Guid Id, KycDocumentType DocumentType, KycStatus Status, Guid? ReviewedByUserId,
    DateTime? UploadedAt, DateTime? ReviewedAt, string? RejectionReason);

// Merchant-facing response — never carries the reviewing staff member's id.
public record MerchantPortalKycDocumentResponse(Guid Id, KycDocumentType DocumentType, KycStatus Status,
    DateTime? UploadedAt, DateTime? ReviewedAt, string? RejectionReason);

public record ReviewKycDocumentRequest(KycStatus Status, string? RejectionReason = null);
