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
  reviewedByUserId: string | null;
}
