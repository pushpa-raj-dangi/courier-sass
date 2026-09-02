"use client";

import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useRiderPickupScan } from "@/features/riders/hooks/useRiderPickupScan";

export function PickupScanControl({ riderId, awb }: { riderId: string; awb: string }) {
  const scanPickup = useRiderPickupScan(riderId);

  if (scanPickup.isSuccess) {
    return <p className="text-sm text-muted-foreground">Picked up.</p>;
  }

  return (
    <div className="space-y-1">
      <Button type="button" size="sm" disabled={scanPickup.isPending} onClick={() => scanPickup.mutate(awb)}>
        {scanPickup.isPending ? <Spinner className="h-4 w-4" /> : "Scan Pickup"}
      </Button>
      {scanPickup.isError && (
        <FieldError id={`pickup-scan-error-${awb}`}>Couldn&apos;t confirm pickup. Try again.</FieldError>
      )}
    </div>
  );
}
