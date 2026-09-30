import { KpiTile } from "@/components/page/KpiTile";

// Kept for existing callers; new code uses KpiTile directly.
export function StatTile({ label, value }: { label: string; value: string | number }) {
  return <KpiTile label={label} value={value} />;
}
