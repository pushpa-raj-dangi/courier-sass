import { Package } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

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
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 font-display text-base">
          <Package className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          Today&apos;s Shipments
        </CardTitle>
      </CardHeader>
      <CardContent className="grid grid-cols-2 gap-6 sm:grid-cols-4">
        {FUNNEL_BUCKETS.map((bucket) => {
          const count = bucket.statuses.reduce((sum, status) => sum + (data[status] ?? 0), 0);
          return (
            <div key={bucket.label} className="flex flex-col gap-1">
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
        })}
      </CardContent>
    </Card>
  );
}
