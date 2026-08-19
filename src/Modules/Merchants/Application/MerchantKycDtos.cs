using Courier.Merchants.Domain;

namespace Courier.Merchants.Application;

public record MerchantKycDocumentResponse(Guid Id, KycDocumentType DocumentType, KycStatus Status, Guid? ReviewedByUserId);
public record ReviewKycDocumentRequest(KycStatus Status);
