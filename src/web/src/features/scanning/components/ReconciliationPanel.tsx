"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle, AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { Skeleton } from "@/components/ui/skeleton";
import { ErrorState } from "@/components/states/ErrorState";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useMissingShipments } from "@/features/manifests/hooks/useMissingShipments";
import { useCloseManifest } from "@/features/manifests/hooks/useCloseManifest";
import type { Manifest } from "@/features/manifests/types";

export function ReconciliationPanel({ manifest }: { manifest: Manifest }) {
  const missing = useMissingShipments(manifest.id, true);
  const close = useCloseManifest(manifest.id);
  const canClose = usePermission("manifest.close");
  const [showAll, setShowAll] = useState(false);

  const pending = missing.data ?? [];
  const verified = Math.max(0, manifest.shipmentCount - pending.length);
  const share = manifest.shipmentCount ? verified / manifest.shipmentCount : 0;
  const visible = showAll ? pending : pending.slice(0, 5);

  return (
    <section aria-label="Manifest reconciliation" className="space-y-4 rounded-xl border border-border bg-card p-5">
      <h2 className="font-display text-[15px] font-semibold">Manifest reconciliation</h2>
      {missing.isLoading && <Skeleton className="h-24 w-full" />}
      {missing.isError && <ErrorState title="Couldn't load missing parcels" description="Reconciliation is unavailable right now." onRetry={() => missing.refetch()} />}
      {missing.data && (
        <>
          <div className="flex items-baseline gap-2">
            <span className="text-4xl font-semibold tabular-nums tracking-tight">{verified}</span>
            <span className="text-base text-muted-foreground">/ {manifest.shipmentCount} verified</span>
          </div>
          <div className="h-2.5 overflow-hidden rounded-full bg-muted" aria-hidden="true">
            <div className="h-full rounded-full bg-status-delivered" style={{ width: `${Math.round(share * 100)}%` }} />
          </div>
          <div className="space-y-2">
            <div className="flex items-baseline justify-between">
              <h3 className="text-sm font-semibold">Still expected</h3>
              <span className="text-xs text-muted-foreground">{pending.length} parcels</span>
            </div>
            <ul className="text-[12.5px]">
              {visible.map((p) => (
                <li key={p.shipmentId} className="border-b border-dashed border-border py-1.5 font-mono">
                  {p.awb}
                </li>
              ))}
            </ul>
            {!showAll && pending.length > 5 && (
              <Button type="button" variant="link" className="h-auto p-0 text-[12.5px]" onClick={() => setShowAll(true)}>
                {`Show all ${pending.length}`}
              </Button>
            )}
          </div>
          {canClose && (
            <AlertDialog>
              <AlertDialogTrigger asChild>
                <Button className="w-full" disabled={close.isPending}>Close manifest</Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Close {manifest.code}?</AlertDialogTitle>
                  <AlertDialogDescription>
                    {pending.length > 0
                      ? `${pending.length} parcels have not been scanned and will be recorded as missing.`
                      : "Every parcel on this manifest has been verified."}
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>Keep scanning</AlertDialogCancel>
                  <AlertDialogAction onClick={() => close.mutate()}>
                    {pending.length > 0 ? `Close and flag ${pending.length} missing` : "Close manifest"}
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          )}
        </>
      )}
    </section>
  );
}
