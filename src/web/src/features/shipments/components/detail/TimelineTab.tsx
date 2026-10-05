import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { eventTitle } from "@/features/shipments/lib/eventLabels";
import { resolveStatusConfig } from "@/components/status-badge/statusConfig";
import type { ShipmentEvent } from "@/features/shipments/types";

export function TimelineTab({ events }: { events: ShipmentEvent[] }) {
  const names = usePeopleNames();
  const newestFirst = [...events].sort((a, b) => b.timestamp.localeCompare(a.timestamp) || b.id.localeCompare(a.id));
  if (newestFirst.length === 0) return <p className="py-6 text-center text-sm text-muted-foreground">No events recorded yet.</p>;
  return (
    <ol aria-label="Timeline" className="flex flex-col">
      {newestFirst.map((e) => {
        const actor = e.riderId ? names.riderName(e.riderId) : e.userId ? names.userName(e.userId) : "System";
        return (
          <li key={e.id} className="flex gap-3.5">
            <div className="flex w-3.5 shrink-0 flex-col items-center">
              <span className="mt-1 h-2.5 w-2.5 rounded-full" style={{ backgroundColor: `hsl(var(--${resolveStatusConfig(e.newStatus).token}))` }} aria-hidden="true" />
              <span className="w-px flex-1 bg-border" aria-hidden="true" />
            </div>
            <div className="flex flex-1 flex-col gap-0.5 pb-3.5">
              <div className="flex justify-between gap-3">
                <span className="text-[13.5px] font-semibold">{eventTitle(e.eventType)}</span>
                <time dateTime={e.timestamp} className="whitespace-nowrap text-xs text-muted-foreground">
                  {new Date(e.timestamp).toLocaleString()}
                </time>
              </div>
              <span className="text-[12.5px] text-foreground/80">{e.branchId ? names.branchName(e.branchId) : "—"}</span>
              <span className="text-xs text-muted-foreground">{actor}</span>
              {e.notes && <span className="text-xs text-muted-foreground">{e.notes}</span>}
            </div>
          </li>
        );
      })}
    </ol>
  );
}
