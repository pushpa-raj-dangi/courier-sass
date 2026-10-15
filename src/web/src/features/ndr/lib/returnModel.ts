import type { ReturnShipment } from "@/features/ndr/types";
import type { ReturnShipmentSummary } from "@/features/ndr/hooks/useReturnShipmentsSummary";

export const RETURN_STAGES = [
  { key: "RtoInitiated", label: "RTO initiated", token: "status-rto" },
  { key: "RtoInTransit", label: "In transit back", token: "status-rto" },
  { key: "RtoAtOrigin", label: "At origin", token: "status-rto" },
  { key: "RtoDelivered", label: "Delivered to merchant", token: "status-delivered" },
  { key: "PendingConfirmation", label: "Pending confirmation", token: "status-out-for-delivery" },
] as const;

export type ReturnStageKey = (typeof RETURN_STAGES)[number]["key"];

export function parseReturnStage(value: string | null): ReturnStageKey | undefined {
  return RETURN_STAGES.find((s) => s.key === value)?.key;
}

/** Pending confirmation = not yet confirmed by the merchant; the others match the live status. */
export function matchesReturnStage(r: ReturnShipment, stage: ReturnStageKey): boolean {
  return stage === "PendingConfirmation" ? r.merchantConfirmedAt === null : r.currentStatus === stage;
}

export function returnStageCount(summary: ReturnShipmentSummary, stage: ReturnStageKey): number {
  return stage === "PendingConfirmation" ? summary.pendingConfirmation : summary.byRtoStage[stage] ?? 0;
}

// NdrService.ConfirmReturnShipmentAsync moves the original shipment RtoAtOrigin → RtoDelivered.
export function canHandBack(r: ReturnShipment): boolean {
  return r.currentStatus === "RtoAtOrigin" && r.merchantConfirmedAt === null;
}
