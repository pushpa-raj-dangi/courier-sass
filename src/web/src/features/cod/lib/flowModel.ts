import type { CodFlow, CodFlowStage, CodFlowStageKey } from "@/features/cod/types";
import type { CodTab } from "@/features/cod/lib/codTabs";

export const FLOW_LABELS: Record<CodFlowStageKey, string> = {
  expected: "Expected",
  collected: "Collected",
  riderDeposited: "Rider deposited",
  branchReconciled: "Branch reconciled",
  financeVerified: "Finance verified",
  settled: "Settled to merchants",
};

/** The tab where the money waiting at each stage gets moved on. */
export const STAGE_TAB: Record<CodFlowStageKey, CodTab> = {
  expected: "collections",
  collected: "rider-deposits",
  riderDeposited: "branch-settlements",
  branchReconciled: "branch-settlements",
  financeVerified: "merchant-settlements",
  settled: "merchant-settlements",
};

/** The stage just before the largest drop in amount (leftmost on ties); null when nothing drops. */
export function pickBottleneck(stages: CodFlowStage[]): CodFlowStageKey | null {
  let best: CodFlowStageKey | null = null;
  let bestDrop = 0;
  for (let i = 0; i < stages.length - 1; i++) {
    const drop = stages[i].amount - stages[i + 1].amount;
    if (drop > bestDrop) {
      bestDrop = drop;
      best = stages[i].key;
    }
  }
  return best;
}

export function stageAmount(flow: CodFlow | undefined, key: CodFlowStageKey): number | undefined {
  return flow?.stages.find((s) => s.key === key)?.amount;
}
