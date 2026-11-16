import { Skeleton } from "@/components/ui/skeleton";
import { formatRs } from "@/features/cod/lib/codFormat";
import { firstName } from "@/features/rider-app/lib/runModel";
import { progressShare } from "@/features/riders/lib/riderModel";
import type { MyToday } from "@/features/riders/types";

const TILE = "space-y-1.5 rounded-lg border border-sidebar-border bg-sidebar-accent p-3";

export function RunHeader({ name, today, isLoading }: { name?: string; today?: MyToday; isLoading: boolean }) {
  const first = name ? firstName(name) : "";
  return (
    <header className="space-y-4 bg-sidebar px-4 pb-4 pt-5 text-sidebar-foreground">
      <div className="space-y-0.5">
        <p className="text-xs text-sidebar-muted">{today?.branchName ? `Run · ${today.branchName}` : "Today's run"}</p>
        <h1 className="font-display text-xl font-semibold">{first ? `Namaste, ${first}` : "Namaste"}</h1>
      </div>
      <div className="grid grid-cols-2 gap-2.5">
        <div className={TILE}>
          <p className="text-xs text-sidebar-muted">Done</p>
          {isLoading || !today ? (
            <Skeleton role="status" aria-label="Loading today's progress" className="h-7 w-20" />
          ) : !today.run ? (
            <p className="text-sm">No run today</p>
          ) : (
            <>
              <p className="text-2xl font-semibold tabular-nums">
                {today.counts.completed}
                <span className="ml-1 text-sm font-normal text-sidebar-muted">{`/ ${today.counts.assigned}`}</span>
              </p>
              <span className="block h-1 rounded-full bg-sidebar-border" aria-hidden="true">
                <span
                  className="block h-1 rounded-full bg-status-delivered"
                  style={{ width: `${Math.round(progressShare(today.counts.completed, today.counts.assigned) * 100)}%` }}
                />
              </span>
            </>
          )}
        </div>
        <div className={TILE}>
          <p className="text-xs text-sidebar-muted">Cash in hand</p>
          {isLoading || !today ? (
            <Skeleton role="status" aria-label="Loading cash in hand" className="h-7 w-24" />
          ) : today.cash ? (
            <>
              <p className="text-2xl font-semibold tabular-nums">{formatRs(today.cash.amount)}</p>
              <p className="text-xs text-sidebar-muted">{`from ${today.cash.parcelCount} COD ${today.cash.parcelCount === 1 ? "parcel" : "parcels"}`}</p>
            </>
          ) : (
            <p className="text-sm">No cash held</p>
          )}
        </div>
      </div>
    </header>
  );
}
