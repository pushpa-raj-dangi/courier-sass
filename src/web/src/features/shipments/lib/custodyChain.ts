import type { ShipmentEvent } from "@/features/shipments/types";

export interface CustodySegment {
  holderKey: string;
  riderId: string | null;
  userId: string | null;
  branchId: string | null;
  from: string;
  to: string | null;
  eventCount: number;
}

// A rider holds a parcel personally; otherwise the branch holds it (whichever staff member
// scanned it). Events with neither keep the previous holder.
function holderOf(e: ShipmentEvent): string | null {
  if (e.riderId) return `rider:${e.riderId}`;
  if (e.branchId) return `branch:${e.branchId}`;
  return null;
}

function sorted(events: ShipmentEvent[]): ShipmentEvent[] {
  return [...events].sort((a, b) => a.timestamp.localeCompare(b.timestamp) || a.id.localeCompare(b.id));
}

export function buildCustodyChain(events: ShipmentEvent[]): CustodySegment[] {
  const chain: CustodySegment[] = [];
  for (const e of sorted(events)) {
    const key = holderOf(e) ?? chain[chain.length - 1]?.holderKey ?? "unknown";
    const current = chain[chain.length - 1];
    if (current && current.holderKey === key) {
      current.eventCount += 1;
      current.userId = e.userId ?? current.userId;
      continue;
    }
    if (current) current.to = e.timestamp;
    chain.push({ holderKey: key, riderId: e.riderId, userId: e.userId, branchId: e.branchId, from: e.timestamp, to: null, eventCount: 1 });
  }
  return chain;
}

export function custodySince(events: ShipmentEvent[]): string | null {
  const chain = buildCustodyChain(events);
  return chain.length ? chain[chain.length - 1].from : null;
}

export function formatDuration(fromIso: string, toIso: string | null, now: Date = new Date()): string {
  const end = toIso ? new Date(toIso).getTime() : now.getTime();
  const minutes = Math.max(0, Math.floor((end - new Date(fromIso).getTime()) / 60000));
  const days = Math.floor(minutes / 1440);
  const hours = Math.floor((minutes % 1440) / 60);
  const mins = minutes % 60;
  if (days > 0) return `${days} d ${hours} h`;
  if (hours > 0) return `${hours} h ${mins} m`;
  return `${mins} m`;
}
