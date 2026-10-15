import { describe, expect, it } from "vitest";
import { canHandBack, matchesReturnStage, parseReturnStage, returnStageCount } from "./returnModel";
import type { ReturnShipment } from "@/features/ndr/types";

const base: ReturnShipment = {
  id: "r1", originalShipmentId: "s1", originalAwb: "KTM-1", returnAwb: "RTO-20261015-001", rtoReason: "Refused",
  originBranchId: "b2", destinationBranchId: "b1", currentStatus: "RtoAtOrigin", currentBranchId: "b1", merchantConfirmedAt: null,
};

describe("returnModel", () => {
  it("parses stages", () => {
    expect(parseReturnStage("RtoAtOrigin")).toBe("RtoAtOrigin");
    expect(parseReturnStage("PendingConfirmation")).toBe("PendingConfirmation");
    expect(parseReturnStage("Delivered")).toBeUndefined();
  });

  it("matches rows to a stage; pending confirmation means not yet confirmed", () => {
    expect(matchesReturnStage(base, "RtoAtOrigin")).toBe(true);
    expect(matchesReturnStage(base, "RtoInTransit")).toBe(false);
    expect(matchesReturnStage(base, "PendingConfirmation")).toBe(true);
    expect(matchesReturnStage({ ...base, merchantConfirmedAt: "2026-10-15T00:00:00Z" }, "PendingConfirmation")).toBe(false);
  });

  it("reads stage counts from the summary", () => {
    const summary = { byRtoStage: { RtoAtOrigin: 3 }, merchantConfirmed: 1, pendingConfirmation: 7 };
    expect(returnStageCount(summary, "RtoAtOrigin")).toBe(3);
    expect(returnStageCount(summary, "RtoInitiated")).toBe(0);
    expect(returnStageCount(summary, "PendingConfirmation")).toBe(7);
  });

  it("allows hand-back only at origin and before confirmation", () => {
    expect(canHandBack(base)).toBe(true);
    expect(canHandBack({ ...base, currentStatus: "RtoDelivered" })).toBe(false);
    expect(canHandBack({ ...base, merchantConfirmedAt: "2026-10-15T00:00:00Z" })).toBe(false);
  });
});
