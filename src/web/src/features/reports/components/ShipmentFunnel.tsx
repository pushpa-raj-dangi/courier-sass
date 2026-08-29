import { Package } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

const FUNNEL_BUCKETS: Array<{ label: string; token: string; statuses: string[] }> = [
  { label: "Created", token: "status-created", statuses: ["Created", "PickupAssigned"] },
  { label: "Picked Up", token: "status-in-transit", statuses: ["PickedUp"] },
  {
    label: "In Transit",
    token: "status-in-transit",
    statuses: ["AtOriginBranch", "Sorted", "InTransit", "AtDestinationBranch"],
  },
  { label: "Out for Delivery", token: "status-out-for-delivery", statuses: ["OutForDelivery"] },
  { label: "Delivered", token: "status-delivered", statuses: ["Delivered"] },
  { label: "NDR", token: "status-ndr", statuses: ["DeliveryFailed", "NdrCreated"] },
  {
    label: "RTO",
    token: "status-rto",
    statuses: ["RtoInitiated", "RtoInTransit", "RtoAtOrigin", "RtoDelivered"],
  },
  { label: "Cancelled", token: "status-cancelled", statuses: ["Cancelled"] },
];

export function ShipmentFunnel({ data }: { data: Record<string, number> }) {
  const firstRow = FUNNEL_BUCKETS.slice(0, 4);
  const secondRow = FUNNEL_BUCKETS.slice(4);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-3 font-display text-base">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted">
            <Package className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          </span>
          Today&apos;s Shipments
        </CardTitle>
        <CardDescription>Shipment activity for today</CardDescription>
      </CardHeader>
      <CardContent className="space-y-6">
        <div className="grid grid-cols-2 gap-6 sm:grid-cols-4">
          {firstRow.map((bucket) => (
            <FunnelTile key={bucket.label} bucket={bucket} count={sumStatuses(data, bucket.statuses)} />
          ))}
        </div>
        <div className="border-t border-border" />
        <div className="grid grid-cols-2 gap-6 sm:grid-cols-4">
          {secondRow.map((bucket) => (
            <FunnelTile key={bucket.label} bucket={bucket} count={sumStatuses(data, bucket.statuses)} />
          ))}
        </div>
      </CardContent>
    </Card>
  );
}

function sumStatuses(data: Record<string, number>, statuses: string[]): number {
  return statuses.reduce((sum, status) => sum + (data[status] ?? 0), 0);
}

function FunnelTile({ bucket, count }: { bucket: { label: string; token: string }; count: number }) {
  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-1.5">
        <span
          className="h-2 w-2 rounded-full"
          style={{ backgroundColor: `hsl(var(--${bucket.token}))` }}
          aria-hidden="true"
        />
        <span className="text-xs text-muted-foreground">{bucket.label}</span>
      </div>
      <span className="font-sans text-2xl font-semibold text-foreground">{count}</span>
    </div>
  );
}
