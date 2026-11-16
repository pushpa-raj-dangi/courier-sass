"use client";

import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useUpdateDeliveryZone } from "@/features/branches/hooks/useUpdateDeliveryZone";
import type { Branch } from "@/features/branches/types";
import { useDeliveryZones } from "@/features/geography/hooks/useDeliveryZones";

/** Branch Sheet footer: the existing delivery-zone action (PUT /branches/{id}/delivery-zone). */
export function BranchZoneControl({ branch }: { branch: Branch }) {
  const { data: zones } = useDeliveryZones();
  const updateZone = useUpdateDeliveryZone(branch.id);
  const canUpdate = usePermission("branch.update", { branchId: branch.id });
  if (!canUpdate) return null;

  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor="branch-delivery-zone" className="text-xs text-muted-foreground">Change delivery zone</Label>
      <Select
        value={branch.deliveryZoneId ?? undefined}
        onValueChange={(zoneId) => updateZone.mutate(zoneId)}
        disabled={updateZone.isPending}
      >
        <SelectTrigger id="branch-delivery-zone" aria-label="Delivery zone" className="w-56">
          <SelectValue placeholder="Not assigned" />
        </SelectTrigger>
        <SelectContent>
          {zones?.map((z) => (
            <SelectItem key={z.id} value={z.id}>{z.name}</SelectItem>
          ))}
        </SelectContent>
      </Select>
      {updateZone.isError && <p role="alert" className="text-xs text-destructive">Couldn&apos;t change the delivery zone. Try again.</p>}
    </div>
  );
}
