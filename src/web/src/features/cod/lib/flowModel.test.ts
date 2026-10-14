import { describe, expect, it } from "vitest";
import { FLOW_LABELS, STAGE_TAB, pickBottleneck, stageAmount } from "./flowModel";
import type { CodFlow, CodFlowStage } from "@/features/cod/types";

const stages = (amounts: number[]): CodFlowStage[] =>
  (["expected", "collected", "riderDeposited", "branchReconciled", "financeVerified", "settled"] as const).map((key, i) => ({ key, amount: amounts[i], count: i }));

describe("pickBottleneck", () => {
  it("returns the stage before the largest drop", () => {
    expect(pickBottleneck(stages([100, 90, 40, 35, 30, 20]))).toBe("collected");
  });
  it("prefers the leftmost on ties", () => {
    expect(pickBottleneck(stages([100, 80, 60, 60, 60, 60]))).toBe("expected");
  });
  it("returns null when nothing drops", () => {
    expect(pickBottleneck(stages([50, 50, 50, 50, 50, 50]))).toBeNull();
    expect(pickBottleneck(stages([0, 0, 0, 0, 0, 0]))).toBeNull();
  });
});

describe("flow helpers", () => {
  it("labels and routes every stage", () => {
    expect(FLOW_LABELS.settled).toBe("Settled to merchants");
    expect(STAGE_TAB.collected).toBe("rider-deposits");
    expect(STAGE_TAB.riderDeposited).toBe("branch-settlements");
    expect(STAGE_TAB.financeVerified).toBe("merchant-settlements");
    expect(STAGE_TAB.expected).toBe("collections");
  });
  it("reads a stage amount, undefined when missing", () => {
    const flow: CodFlow = { stages: stages([9, 8, 7, 6, 5, 4]) };
    expect(stageAmount(flow, "financeVerified")).toBe(5);
    expect(stageAmount(undefined, "financeVerified")).toBeUndefined();
  });
});
