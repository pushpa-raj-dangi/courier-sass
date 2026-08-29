import { Package } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { HorizontalBarList } from "@/components/charts/HorizontalBarList";

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
  const rows = FUNNEL_BUCKETS.map((bucket) => ({
    key: bucket.label,
    label: bucket.label,
    value: sumStatuses(data, bucket.statuses),
    color: `hsl(var(--${bucket.token}))`,
  }));

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
      <CardContent>
        <HorizontalBarList rows={rows} />
      </CardContent>
    </Card>
  );
}

function sumStatuses(data: Record<string, number>, statuses: string[]): number {
  return statuses.reduce((sum, status) => sum + (data[status] ?? 0), 0);
}
