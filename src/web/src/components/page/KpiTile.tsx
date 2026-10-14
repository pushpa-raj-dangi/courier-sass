import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

export type KpiDeltaTone = "positive" | "negative" | "neutral";

// Tone colors are contrast-checked in tokens.contrast.test.ts (status-delivered, destructive on
// card). The delta text itself always carries the direction ("+0.4 pts"), so tone is a
// confirmation signal, never the only one.
const TONE_CLASS: Record<KpiDeltaTone, string> = {
  positive: "text-status-delivered",
  negative: "text-destructive",
  neutral: "text-muted-foreground",
};

export function KpiTile({
  label,
  value,
  delta,
  note,
}: {
  label: string;
  value: ReactNode;
  delta?: { text: string; tone: KpiDeltaTone };
  note?: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-1.5 rounded-xl border border-border bg-card p-4">
      <span className="text-sm text-muted-foreground">{label}</span>
      <div className="flex flex-wrap items-baseline gap-2">
        <span className="text-2xl font-semibold tabular-nums tracking-tight text-foreground">{value}</span>
        {delta && (
          <span data-tone={delta.tone} className={cn("text-xs font-medium", TONE_CLASS[delta.tone])}>
            {delta.text}
          </span>
        )}
      </div>
      {note && <span className="text-2xs text-muted-foreground">{note}</span>}
    </div>
  );
}
