import type { KycDocumentType, MerchantKycDocument } from "../types";

export const REQUIRED_KYC_TYPES: readonly KycDocumentType[] = [
  "CitizenshipDocument",
  "PanCard",
  "BusinessRegistrationCertificate",
  "BankAccountProof",
];

export const KYC_TYPE_LABEL: Record<KycDocumentType, string> = {
  CitizenshipDocument: "Citizenship document",
  PanCard: "PAN card",
  BusinessRegistrationCertificate: "Business registration certificate",
  BankAccountProof: "Bank account proof",
  Other: "Other",
};

export type KycRowState = "Approved" | "InReview" | "Rejected" | "NotUploaded";
export interface KycRow {
  type: KycDocumentType;
  state: KycRowState;
  doc: MerchantKycDocument | null;
}

// Legacy rows have no uploadedAt; they sort before every dated upload.
const time = (d: MerchantKycDocument) => (d.uploadedAt ? new Date(d.uploadedAt).getTime() : Number.NEGATIVE_INFINITY);

// A document at a later list position wins a tie (>=).
export function latestPerType(docs: MerchantKycDocument[]): Map<KycDocumentType, MerchantKycDocument> {
  const latest = new Map<KycDocumentType, MerchantKycDocument>();
  for (const d of docs) {
    const current = latest.get(d.documentType);
    if (!current || time(d) >= time(current)) latest.set(d.documentType, d);
  }
  return latest;
}

export function requiredRows(docs: MerchantKycDocument[]): KycRow[] {
  const latest = latestPerType(docs);
  return REQUIRED_KYC_TYPES.map((type) => {
    const doc = latest.get(type) ?? null;
    const state: KycRowState = !doc ? "NotUploaded" : doc.status === "Pending" ? "InReview" : doc.status;
    return { type, state, doc };
  });
}

export function otherDocs(docs: MerchantKycDocument[]): MerchantKycDocument[] {
  return docs
    .map((d, i) => ({ d, i }))
    .filter(({ d }) => d.documentType === "Other")
    .sort((a, b) => time(b.d) - time(a.d) || b.i - a.i)
    .map(({ d }) => d);
}

export function kycProgress(rows: KycRow[]): { approved: number; rejected: number; inReview: number; total: 4 } {
  const count = (s: KycRowState) => rows.filter((r) => r.state === s).length;
  return { approved: count("Approved"), rejected: count("Rejected"), inReview: count("InReview"), total: 4 };
}
