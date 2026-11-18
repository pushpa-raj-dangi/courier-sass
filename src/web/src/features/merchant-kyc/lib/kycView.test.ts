import { describe, expect, it } from "vitest";
import type { MerchantKycDocument } from "../types";
import { kycProgress, latestPerType, otherDocs, requiredRows } from "./kycView";

const doc = (over: Partial<MerchantKycDocument>): MerchantKycDocument => ({
  id: "d",
  documentType: "PanCard",
  status: "Pending",
  uploadedAt: "2026-11-10T00:00:00Z",
  reviewedAt: null,
  rejectionReason: null,
  ...over,
});

describe("latestPerType", () => {
  it("prefers a newer Pending over a legacy Rejected row with null uploadedAt", () => {
    const legacy = doc({ id: "old", status: "Rejected", uploadedAt: null });
    const fresh = doc({ id: "new", status: "Pending" });
    expect(latestPerType([fresh, legacy]).get("PanCard")?.id).toBe("new");
    expect(latestPerType([legacy, fresh]).get("PanCard")?.id).toBe("new");
  });

  it("with two null uploadedAt the later list position wins", () => {
    const a = doc({ id: "a", uploadedAt: null });
    const b = doc({ id: "b", uploadedAt: null });
    expect(latestPerType([a, b]).get("PanCard")?.id).toBe("b");
  });

  it("an equal uploadedAt tie goes to the later list position", () => {
    const a = doc({ id: "a" });
    const b = doc({ id: "b" });
    expect(latestPerType([a, b]).get("PanCard")?.id).toBe("b");
  });
});

describe("requiredRows", () => {
  it("returns four rows in order; missing is NotUploaded and Pending is InReview", () => {
    const rows = requiredRows([doc({ documentType: "PanCard", status: "Pending" })]);
    expect(rows.map((r) => r.type)).toEqual([
      "CitizenshipDocument",
      "PanCard",
      "BusinessRegistrationCertificate",
      "BankAccountProof",
    ]);
    expect(rows.map((r) => r.state)).toEqual(["NotUploaded", "InReview", "NotUploaded", "NotUploaded"]);
    expect(rows[0].doc).toBeNull();
  });
});

describe("otherDocs", () => {
  it("lists only Other documents, newest first", () => {
    const o1 = doc({ id: "o1", documentType: "Other", uploadedAt: "2026-11-01T00:00:00Z" });
    const o2 = doc({ id: "o2", documentType: "Other", uploadedAt: "2026-11-05T00:00:00Z" });
    expect(otherDocs([o1, doc({ id: "p" }), o2]).map((d) => d.id)).toEqual(["o2", "o1"]);
  });
});

describe("kycProgress", () => {
  it("counts a mixed fixture", () => {
    const rows = requiredRows([
      doc({ documentType: "CitizenshipDocument", status: "Approved" }),
      doc({ documentType: "PanCard", status: "Approved" }),
      doc({ documentType: "BusinessRegistrationCertificate", status: "Rejected" }),
      doc({ documentType: "BankAccountProof", status: "Pending" }),
    ]);
    expect(kycProgress(rows)).toEqual({ approved: 2, rejected: 1, inReview: 1, total: 4 });
  });
});
