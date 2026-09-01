"use client";

import { Button } from "@/components/ui/button";
import { useNdrs } from "@/features/ndr/hooks/useNdrs";
import { useReturnShipments } from "@/features/ndr/hooks/useReturnShipments";
import { useNdrPanelStore } from "@/features/ndr/store/ndrPanelStore";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";

// Lives in features/shipments (not features/ndr) since it's rendered from and scoped to
// ShipmentDetailSheet — matches this codebase's existing precedent of cross-feature imports where
// the consuming feature owns the component (e.g. ShipmentDetailSheet already imports useBranches
// from features/branches).
export function ShipmentNdrRtoLinks({ shipmentId }: { shipmentId: string }) {
  const { data: ndrs } = useNdrs({ shipmentId, pageSize: 50 });
  const { data: returnShipments } = useReturnShipments(shipmentId);
  const openNdrDetail = useNdrPanelStore((s) => s.openDetail);
  const openReturnShipmentDetail = useReturnShipmentPanelStore((s) => s.openDetail);

  const latestNdr = ndrs?.items.find((n) => n.status !== "Resolved") ?? ndrs?.items[0];
  const returnShipment = returnShipments?.[0];

  if (!latestNdr && !returnShipment) {
    return null;
  }

  return (
    <div className="flex gap-2">
      {latestNdr && (
        <Button type="button" variant="outline" size="sm" onClick={() => openNdrDetail(latestNdr.id)}>
          View NDR
        </Button>
      )}
      {returnShipment && (
        <Button type="button" variant="outline" size="sm" onClick={() => openReturnShipmentDetail(returnShipment.id)}>
          View Return Shipment
        </Button>
      )}
    </div>
  );
}
