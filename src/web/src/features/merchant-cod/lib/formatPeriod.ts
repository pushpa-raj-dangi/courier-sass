import { formatDate } from "@/features/cod/lib/codFormat";

const dayMonth = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short" });

/** "1 Nov–15 Nov 2026" (d MMM–d MMM yyyy). */
export function formatPeriod(fromIso: string, toIso: string): string {
  return `${dayMonth.format(new Date(fromIso))}–${formatDate(toIso)}`;
}

export function formatPaidOn(settledAt: string | null): string {
  return settledAt ? formatDate(settledAt) : "—";
}
