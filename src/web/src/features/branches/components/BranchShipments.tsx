import { Package } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import type {
  BranchShipmentCounts,
  BranchManifestCounts,
  DeliverySuccessStats,
} from "@/features/reports/types";

function formatRate(successes: number, attempts: number): string {
  return attempts === 0 ? "—" : `${((successes / attempts) * 100).toFixed(1)}%`;
}

export function BranchShipments({
  data,
}: {
  data: {
    shipmentCounts: BranchShipmentCounts;
    manifestCounts: BranchManifestCounts;
    todaysDeliveries: DeliverySuccessStats;
  };
}) {
  const tiles = [
    { label: "Outgoing", value: data.shipmentCounts.outgoing },
    { label: "Pending Sorting", value: data.shipmentCounts.pendingSorting },
    { label: "Pending Dispatch", value: data.shipmentCounts.pendingDispatch },
    { label: "Manifests In Transit", value: data.manifestCounts.inTransitTowardBranch },
    { label: "Manifests Recently Closed", value: data.manifestCounts.recentlyClosed },
    {
      label: "Today's Delivery Success",
      value: formatRate(data.todaysDeliveries.successes, data.todaysDeliveries.attempts),
    },
  ];
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-3 font-display text-base">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted">
            <Package className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          </span>
          Shipments &amp; Manifests
        </CardTitle>
        <CardDescription>Operational activity for this branch</CardDescription>
      </CardHeader>
      <CardContent className="grid grid-cols-2 gap-6 sm:grid-cols-3">
        {tiles.map((tile) => (
          <div key={tile.label} className="flex flex-col gap-1">
            <span className="text-xs text-muted-foreground">{tile.label}</span>
            <span className="font-sans text-2xl font-semibold text-foreground">{tile.value}</span>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}
