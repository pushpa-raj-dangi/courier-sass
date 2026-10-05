import type { PipelineStage } from "@/components/page/PipelineStrip";
import { presetDays, toUtcRange, type RangePreset } from "@/lib/dates/rangePresets";
import { sumStatuses } from "@/features/shipments/lib/statusGroups";
import type { BranchPerformance, CodFinancialSummary } from "@/features/reports/types";

const STAGES: { key: string; label: string; statuses: string[]; token: string; href: string }[] = [
  { key: "booked", label: "Booked", statuses: ["Created", "PickupAssigned"], token: "status-created", href: "/shipments?status=Created&status=PickupAssigned" },
  { key: "picked-up", label: "Picked up", statuses: ["PickedUp"], token: "status-in-transit", href: "/shipments?status=PickedUp" },
  { key: "origin", label: "At origin", statuses: ["AtOriginBranch", "Sorted"], token: "status-in-transit", href: "/shipments?status=AtOriginBranch&status=Sorted" },
  { key: "in-transit", label: "In transit", statuses: ["InTransit"], token: "status-in-transit", href: "/shipments?tab=in-transit" },
  { key: "destination", label: "At destination", statuses: ["AtDestinationBranch"], token: "status-in-transit", href: "/shipments?status=AtDestinationBranch" },
  { key: "out-for-delivery", label: "Out for delivery", statuses: ["OutForDelivery"], token: "status-out-for-delivery", href: "/shipments?tab=out-for-delivery" },
];

export function buildPipelineStages(
  counts: Record<string, number> | undefined,
  branchPerformance: Record<string, BranchPerformance> | undefined
): PipelineStage[] {
  const delivered = Object.values(branchPerformance ?? {}).reduce((t, b) => t + (b.deliverySuccesses ?? 0), 0);
  return [
    ...STAGES.map((s) => ({ key: s.key, label: s.label, count: sumStatuses(counts, s.statuses), token: s.token, href: s.href })),
    { key: "delivered", label: "Delivered", count: delivered, token: "status-delivered", caption: "in range" },
  ];
}

export function codSegments(f: CodFinancialSummary) {
  return [
    { key: "settled" as const, label: "Settled", value: Math.max(0, f.settled), token: "status-delivered" },
    { key: "collected" as const, label: "Collected, not settled", value: Math.max(0, f.collected - f.settled), token: "status-out-for-delivery" },
    { key: "uncollected" as const, label: "Not yet collected", value: Math.max(0, f.expected - f.collected), token: "status-created" },
  ];
}

export function formatRate(v: number | null): string {
  return v === null ? "—" : `${(v * 100).toFixed(1)}%`;
}

// .NET TimeSpan JSON: "[d.]hh:mm:ss[.fffffff]".
export function formatTimeSpan(v: string | null): string {
  if (v === null) return "—";
  const m = v.match(/^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})(?:\.\d+)?$/);
  if (!m) return "—";
  const hours = (m[1] ? Number(m[1]) * 24 : 0) + Number(m[2]) + Number(m[3]) / 60;
  return hours >= 24 ? `${(hours / 24).toFixed(1)} days` : `${hours.toFixed(1)} h`;
}

export function openNdrCount(byStatus: Record<string, number> | undefined): number {
  return Object.entries(byStatus ?? {}).reduce((t, [status, n]) => (status === "Resolved" ? t : t + n), 0);
}

export type DashboardRange = { preset: RangePreset | "custom"; from: string; to: string };

const PRESET_LABEL: Record<RangePreset, string> = { today: "Today", "7d": "Last 7 days", "30d": "Last 30 days" };

export function rangeLabel(r: DashboardRange): string {
  if (r.preset !== "custom") return PRESET_LABEL[r.preset];
  if (r.from && r.to) return `${r.from} – ${r.to}`;
  return r.from ? `Since ${r.from}` : r.to ? `Until ${r.to}` : "All time";
}

export function rangeToApi(r: DashboardRange, now: Date = new Date()): { from?: string; to?: string } {
  const days = r.preset === "custom" ? { from: r.from, to: r.to } : presetDays(r.preset, now);
  return toUtcRange(days.from, days.to);
}
