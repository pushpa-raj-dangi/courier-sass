import { Check, X } from "lucide-react";
import { cn } from "@/lib/utils";
import type { ScanLogEntry } from "@/features/scanning/scanSessionReducer";

export function ScanResultCard({ entry }: { entry: ScanLogEntry | null }) {
  return (
    <div
      role="status"
      aria-live="polite"
      className={cn(
        "flex min-h-[96px] items-center gap-4 rounded-xl border p-5",
        !entry && "border-dashed border-border bg-card text-muted-foreground",
        entry?.ok && "border-status-delivered/40 bg-status-delivered/10",
        entry && !entry.ok && "border-destructive/40 bg-destructive/10"
      )}
    >
      {!entry ? (
        <span className="text-sm">Scan a parcel to see the result here.</span>
      ) : (
        <>
          <span className={cn("flex h-12 w-12 shrink-0 items-center justify-center rounded-xl text-white", entry.ok ? "bg-status-delivered" : "bg-destructive")}>
            {entry.ok ? <Check className="h-6 w-6" aria-hidden="true" /> : <X className="h-6 w-6" aria-hidden="true" />}
          </span>
          <div className="flex min-w-0 flex-col gap-1">
            <span className={cn("text-[13px] font-semibold", entry.ok ? "text-status-delivered" : "text-destructive")}>
              {entry.ok ? "Accepted" : "Rejected"}
            </span>
            <span className="truncate font-mono text-xl font-medium sm:text-2xl">{entry.awb}</span>
            <span className="text-[12.5px] text-foreground/80">{entry.text}</span>
          </div>
        </>
      )}
    </div>
  );
}
