import type { RangePreset } from "@/lib/dates/rangePresets";

export const COD_TABS = [
  { key: "collections", label: "Collections" },
  { key: "rider-deposits", label: "Rider deposits" },
  { key: "branch-settlements", label: "Branch settlements" },
  { key: "merchant-settlements", label: "Merchant settlements" },
  { key: "adjustments", label: "Adjustments" },
  { key: "ledger", label: "Ledger" },
] as const;

export type CodTab = (typeof COD_TABS)[number]["key"];

export const DEFAULT_COD_TAB: CodTab = "rider-deposits";

export function parseCodTab(value: string | null): CodTab {
  return COD_TABS.some((t) => t.key === value) ? (value as CodTab) : DEFAULT_COD_TAB;
}

export const RANGE_PRESETS: RangePreset[] = ["today", "7d", "30d"];

export const RANGE_LABELS: Record<RangePreset, string> = { today: "Today", "7d": "Last 7 days", "30d": "Last 30 days" };

export function parseRangePreset(value: string | null): RangePreset {
  return RANGE_PRESETS.includes(value as RangePreset) ? (value as RangePreset) : "7d";
}
