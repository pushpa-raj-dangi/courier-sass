"use client";

import { Spinner } from "@/components/ui/spinner";
import { Button } from "@/components/ui/button";
import { useMissingShipments } from "@/features/manifests/hooks/useMissingShipments";
import { useCloseManifest } from "@/features/manifests/hooks/useCloseManifest";
import type { Manifest } from "@/features/manifests/types";

export function ManifestReceivedSection({ manifest }: { manifest: Manifest }) {
  const { data: missing, isLoading } = useMissingShipments(manifest.id, true);
  const closeManifest = useCloseManifest(manifest.id);

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <span className="text-xs text-muted-foreground">Missing Shipments</span>
        {isLoading && <Spinner className="h-4 w-4" />}
        {!isLoading && missing && missing.length === 0 && (
          <p className="text-sm text-muted-foreground">No missing shipments.</p>
        )}
        {!isLoading && missing && missing.length > 0 && (
          <ul className="space-y-1 text-sm">
            {missing.map((m) => (
              <li key={m.shipmentId} className="text-destructive">{m.awb}</li>
            ))}
          </ul>
        )}
      </div>
      <Button type="button" disabled={closeManifest.isPending} onClick={() => closeManifest.mutate()}>
        {closeManifest.isPending ? <Spinner className="h-4 w-4" /> : "Close Manifest"}
      </Button>
    </div>
  );
}
