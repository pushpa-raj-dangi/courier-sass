const dayMonth = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short" });

/** Local hour: <12 morning, <17 afternoon, else evening. */
export function greetingFor(hour: number): "morning" | "afternoon" | "evening" {
  if (hour < 12) return "morning";
  if (hour < 17) return "afternoon";
  return "evening";
}

export function firstName(fullName: string | undefined): string {
  return fullName?.trim().split(/\s+/)[0] ?? "";
}

/** Sum of the counts, or null when any of them failed to load. */
export function sumCounts(counts: Array<number | null>): number | null {
  return counts.some((c) => c === null) ? null : counts.reduce<number>((a, c) => a + (c ?? 0), 0);
}

export function formatDayMonth(iso: string): string {
  return dayMonth.format(new Date(iso));
}

export function settlementsCaption(count: number): string {
  return `${count} ${count === 1 ? "settlement" : "settlements"} awaiting payout`;
}

export type AttentionRow = { key: "needsAttention" | "returning"; text: string };

/** Rows to show; empty when both counts are known to be zero. Unknown counts render as "—". */
export function attentionRows(needsAttention: number | null, returning: number | null): AttentionRow[] {
  const rows: AttentionRow[] = [];
  if (needsAttention !== 0) rows.push({ key: "needsAttention", text: `${needsAttention ?? "—"} failed deliveries` });
  if (returning !== 0) rows.push({ key: "returning", text: `${returning ?? "—"} returning to you` });
  return rows;
}
