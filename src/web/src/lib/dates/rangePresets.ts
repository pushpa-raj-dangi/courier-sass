export type RangePreset = "today" | "7d" | "30d";

const PRESET_SPAN_DAYS: Record<RangePreset, number> = { today: 1, "7d": 7, "30d": 30 };

// The user's local calendar day as YYYY-MM-DD (not toISOString, which shifts to UTC).
export function localIsoDay(d: Date): string {
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, "0");
  const dd = String(d.getDate()).padStart(2, "0");
  return `${yyyy}-${mm}-${dd}`;
}

// Inclusive of today: "7d" is today and the 6 days before it.
export function presetDays(preset: RangePreset, now: Date = new Date()): { from: string; to: string } {
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate() - (PRESET_SPAN_DAYS[preset] - 1));
  return { from: localIsoDay(start), to: localIsoDay(now) };
}

// UTC-anchored before leaving the browser — a bare "YYYY-MM-DD" lets .NET's DateTimeOffset
// deserializer default to the API host's local offset, which Npgsql's timestamptz rejects
// (the Frontend Phase F7 bug).
export function toUtcRange(from?: string, to?: string): { from?: string; to?: string } {
  return {
    from: from ? `${from}T00:00:00Z` : undefined,
    to: to ? `${to}T23:59:59Z` : undefined,
  };
}
