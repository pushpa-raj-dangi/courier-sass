import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { buildCustodyChain, formatDuration } from "@/features/shipments/lib/custodyChain";
import type { ShipmentEvent } from "@/features/shipments/types";

export function CustodyTab({ events }: { events: ShipmentEvent[] }) {
  const names = usePeopleNames();
  const chain = buildCustodyChain(events).reverse();
  if (chain.length === 0) return <p className="py-6 text-center text-sm text-muted-foreground">No custody records yet.</p>;
  return (
    <ol aria-label="Custody chain" className="divide-y divide-border rounded-xl border border-border bg-card">
      {chain.map((segment) => (
        <li key={`${segment.holderKey}-${segment.from}`} className="flex flex-col gap-0.5 px-4 py-3">
          <div className="flex justify-between gap-3">
            <span className="text-sm font-semibold">
              {segment.riderId ? names.riderName(segment.riderId) : names.branchName(segment.branchId)}
            </span>
            <span className="text-xs tabular-nums text-muted-foreground">{formatDuration(segment.from, segment.to)}</span>
          </div>
          <span className="text-xs text-muted-foreground">
            {new Date(segment.from).toLocaleString()} → {segment.to ? new Date(segment.to).toLocaleString() : "now"}
          </span>
          {segment.riderId && segment.branchId && <span className="text-xs text-muted-foreground">from {names.branchName(segment.branchId)}</span>}
        </li>
      ))}
    </ol>
  );
}
