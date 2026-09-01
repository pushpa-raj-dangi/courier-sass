"use client";

import { Button } from "@/components/ui/button";
import { useReturnShipments } from "@/features/ndr/hooks/useReturnShipments";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";
import type { Ndr } from "@/features/ndr/types";

export function NdrResolvedSummary({ ndr }: { ndr: Ndr }) {
  const isRto = ndr.resolution === "Rto" || ndr.resolution === "AutoRto";
  const { data: returnShipments } = useReturnShipments(isRto ? ndr.shipmentId : undefined);
  const openDetail = useReturnShipmentPanelStore((s) => s.openDetail);
  const returnShipment = returnShipments?.[0];

  return (
    <div className="space-y-2">
      <p className="text-sm text-muted-foreground">
        Resolved as <span className="font-medium text-foreground">{ndr.resolution}</span>.
      </p>
      {isRto && returnShipment && (
        <Button type="button" variant="outline" size="sm" onClick={() => openDetail(returnShipment.id)}>
          View Return Shipment
        </Button>
      )}
    </div>
  );
}
