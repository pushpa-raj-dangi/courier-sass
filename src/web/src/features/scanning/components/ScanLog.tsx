import { cn } from "@/lib/utils";
import type { ScanLogEntry } from "@/features/scanning/scanSessionReducer";

export function ScanLog({ entries }: { entries: ScanLogEntry[] }) {
  return (
    <section aria-label="Scan log" className="overflow-hidden rounded-xl border border-border bg-card">
      <div className="flex items-center justify-between border-b border-border px-4 py-3">
        <h2 className="font-display text-base font-semibold">Scan log</h2>
        <span className="text-xs text-muted-foreground">{entries.length} scans this session</span>
      </div>
      {entries.length === 0 ? (
        <p className="px-4 py-6 text-center text-sm text-muted-foreground">No scans yet.</p>
      ) : (
        <ol className="max-h-[420px] overflow-y-auto">
          {entries.map((e) => (
            <li key={e.id} className="grid grid-cols-[72px_minmax(0,1fr)] items-center gap-x-3 border-b border-border/60 px-4 py-2.5 text-sm sm:grid-cols-[80px_minmax(0,1.2fr)_minmax(0,2fr)]">
              <time dateTime={e.at} className="font-mono text-xs text-muted-foreground">
                {new Date(e.at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" })}
              </time>
              <span className="font-mono font-medium">{e.awb}</span>
              <span className={cn("col-span-2 flex items-center gap-1.5 text-xs sm:col-span-1", e.ok ? "text-status-delivered" : "text-destructive")}>
                <span className={cn("h-1.5 w-1.5 shrink-0 rounded-full", e.ok ? "bg-status-delivered" : "bg-destructive")} aria-hidden="true" />
                {e.text}
              </span>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
