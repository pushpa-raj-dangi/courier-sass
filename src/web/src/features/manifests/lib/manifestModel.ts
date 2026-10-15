import type { ManifestStatus } from "@/features/manifests/types";

// Five stages, labelled as the backend behaves: there is no "Dispatched" status (dispatch moves
// Sealed → InTransit) and Received means receiving has started.
export const MANIFEST_STAGES: { status: ManifestStatus; label: string; token: string }[] = [
  { status: "Draft", label: "Draft", token: "status-created" },
  { status: "Sealed", label: "Sealed", token: "status-sealed" },
  { status: "InTransit", label: "In transit", token: "status-in-transit" },
  { status: "Received", label: "Receiving", token: "status-received" },
  { status: "Closed", label: "Closed", token: "status-delivered" },
];

const ORDER = MANIFEST_STAGES.map((s) => s.status);

export type ManifestTab = "incoming" | "outgoing" | "all";

export function parseManifestTab(value: string | null): ManifestTab {
  return value === "outgoing" || value === "all" ? value : "incoming";
}

export function parseManifestStatus(value: string | null): ManifestStatus | undefined {
  return ORDER.find((s) => s === value);
}

export function manifestStageLabel(status: ManifestStatus): string {
  return MANIFEST_STAGES.find((s) => s.status === status)?.label ?? status;
}

/** The open stage holding the most manifests (Closed excluded); ties go left; none when all 0. */
export function pickManifestHighlight(counts: Record<string, number>): ManifestStatus | undefined {
  let best: ManifestStatus | undefined;
  let bestCount = 0;
  for (const status of ORDER) {
    if (status === "Closed") continue;
    const count = counts[status] ?? 0;
    if (count > bestCount) {
      best = status;
      bestCount = count;
    }
  }
  return best;
}

/** "118 / 120" (+ missing) once receiving has started; "120 parcels" before. */
export function parcelsSummary(m: { status: ManifestStatus; shipmentCount: number; receivedCount: number }): { primary: string; missing: number } {
  if (m.status === "Received" || m.status === "Closed") {
    return { primary: `${m.receivedCount} / ${m.shipmentCount}`, missing: Math.max(0, m.shipmentCount - m.receivedCount) };
  }
  return { primary: `${m.shipmentCount} ${m.shipmentCount === 1 ? "parcel" : "parcels"}`, missing: 0 };
}

export type StepState = "complete" | "current" | "upcoming";

export function stepperState(current: ManifestStatus, step: ManifestStatus): StepState {
  if (current === "Closed") return "complete";
  const currentIndex = ORDER.indexOf(current);
  const stepIndex = ORDER.indexOf(step);
  if (stepIndex < currentIndex) return "complete";
  return stepIndex === currentIndex ? "current" : "upcoming";
}
