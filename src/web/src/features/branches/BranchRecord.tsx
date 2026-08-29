"use client";

import { Building2 } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Label } from "@/components/ui/label";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useDeliveryZones } from "@/features/geography/hooks/useDeliveryZones";
import { useUpdateDeliveryZone } from "@/features/branches/hooks/useUpdateDeliveryZone";
import type { Branch } from "@/features/branches/types";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function BranchRecord({ branch }: { branch: Branch }) {
  const { data: zones } = useDeliveryZones();
  const updateZone = useUpdateDeliveryZone(branch.id);
  const canUpdate = usePermission("branch.update", { branchId: branch.id });

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-3 font-display text-base">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted">
            <Building2 className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          </span>
          Branch Details
        </CardTitle>
      </CardHeader>
      <CardContent className="grid grid-cols-2 gap-6 sm:grid-cols-4">
        <Field label="Code" value={branch.code} />
        <Field label="Name" value={branch.name} />
        <Field label="Type" value={branch.type} />
        <Field label="Active" value={branch.isActive ? "Yes" : "No"} />
        <Field label="Address" value={branch.addressLine} />
        <Field label="Ward" value={String(branch.wardNumber)} />
        <Field label="Phone" value={branch.phone ?? "—"} />
        <Field label="Operating Hours" value={branch.operatingHours ?? "—"} />
        <div className="col-span-2 flex flex-col gap-1 sm:col-span-4">
          <Label htmlFor="delivery-zone" className="text-xs text-muted-foreground">
            Delivery Zone
          </Label>
          <Select
            value={branch.deliveryZoneId ?? undefined}
            onValueChange={(zoneId) => updateZone.mutate(zoneId)}
            disabled={!canUpdate}
          >
            <SelectTrigger id="delivery-zone" aria-label="Delivery zone" className="max-w-xs">
              <SelectValue placeholder="Not assigned" />
            </SelectTrigger>
            <SelectContent>
              {zones?.map((z) => (
                <SelectItem key={z.id} value={z.id}>
                  {z.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </CardContent>
    </Card>
  );
}
