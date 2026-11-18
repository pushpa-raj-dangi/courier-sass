export type KycDocumentType =
  | "CitizenshipDocument"
  | "PanCard"
  | "BusinessRegistrationCertificate"
  | "BankAccountProof"
  | "Other";
export type KycStatus = "Pending" | "Approved" | "Rejected";

export interface MerchantKycDocument {
  id: string;
  documentType: KycDocumentType;
  status: KycStatus;
  uploadedAt: string | null;
  reviewedAt: string | null;
  rejectionReason: string | null;
}
