"use client";

import { useState } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { FieldError } from "@/components/ui/field-error";
import { useDispatchManifest } from "@/features/manifests/hooks/useDispatchManifest";
import { useRiders } from "@/features/riders/hooks/useRiders";
import type { Manifest } from "@/features/manifests/types";

export function ManifestSealedSection({ manifest, canDispatch = true }: { manifest: Manifest; canDispatch?: boolean }) {
  const [vehiclePlate, setVehiclePlate] = useState("");
  const [driverId, setDriverId] = useState<string | undefined>(undefined);
  const [error, setError] = useState<string | null>(null);
  const { data: riders } = useRiders();
  const dispatchManifest = useDispatchManifest(manifest.id);

  async function handleDispatch() {
    if (!vehiclePlate.trim()) {
      setError("Vehicle plate is required.");
      return;
    }
    setError(null);
    try {
      await dispatchManifest.mutateAsync({ vehiclePlate: vehiclePlate.trim(), driverId });
    } catch {
      setError("Couldn't dispatch this manifest. Check the fields and try again.");
    }
  }

  if (!canDispatch) return <p className="text-sm text-muted-foreground">Sealed and waiting for dispatch.</p>;

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="vehiclePlate">Vehicle Plate</Label>
        <Input
          id="vehiclePlate"
          aria-invalid={!!error}
          aria-describedby={error ? "vehiclePlate-error" : undefined}
          value={vehiclePlate}
          onChange={(e) => setVehiclePlate(e.target.value)}
        />
      </div>
      <div className="space-y-2">
        <Label htmlFor="driverId">Driver</Label>
        <Select value={driverId} onValueChange={setDriverId}>
          <SelectTrigger id="driverId" aria-label="Driver">
            <SelectValue placeholder="Select driver (optional)" />
          </SelectTrigger>
          <SelectContent>
            {riders?.map((r) => (
              <SelectItem key={r.id} value={r.id}>{r.licenseNumber}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      {error && <FieldError id="vehiclePlate-error">{error}</FieldError>}
      <Button type="button" disabled={dispatchManifest.isPending} onClick={handleDispatch}>
        {dispatchManifest.isPending ? <Spinner className="h-4 w-4" /> : "Dispatch"}
      </Button>
    </div>
  );
}
