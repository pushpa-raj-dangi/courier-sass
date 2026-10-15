"use client";

import Link from "next/link";
import { Package, PackageCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { SectionCard } from "@/components/page/SectionCard";
import { ErrorState } from "@/components/states/ErrorState";
import { useMissingShipments } from "@/features/manifests/hooks/useMissingShipments";
import { useManifestShipments } from "@/features/manifests/hooks/useManifestShipments";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import type { Manifest } from "@/features/manifests/types";

const timeFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });

function parcelLabel(p: { awb: string; shipmentId: string }): string {
  return p.awb || p.shipmentId.slice(0, 8);
}

export function ManifestReconciliation({ manifest }: { manifest: Manifest }) {
  const missing = useMissingShipments(manifest.id, true);
  const parcels = useManifestShipments(manifest.id, true);
  const openShipment = useShipmentPanelStore((s) => s.openDetail);
  const closed = manifest.status === "Closed";
  const total = manifest.shipmentCount;
  const received = manifest.receivedCount;
  const share = total > 0 ? received / total : 0;

  return (
    <div className="space-y-4">
      <SectionCard
        title="Receive reconciliation"
        icon={PackageCheck}
        action={closed ? undefined : <Link href="/scan" className="font-medium text-primary hover:underline">Open scan station</Link>}
      >
        <div className="space-y-3">
          <p className="flex items-baseline gap-1.5">
            <span className="text-2xl font-semibold tabular-nums tracking-tight">{received}</span>
            <span className="text-sm text-muted-foreground">{`of ${total} verified`}</span>
          </p>
          <div
            role="progressbar"
            aria-label="Parcels verified"
            aria-valuemin={0}
            aria-valuemax={total}
            aria-valuenow={received}
            className="h-2 overflow-hidden rounded-full bg-muted"
          >
            <div className="h-full rounded-full bg-status-delivered" style={{ width: `${Math.round(share * 100)}%` }} />
          </div>

          <h3 className="pt-1 text-sm font-semibold">
            {closed && missing.data ? `Closed with ${missing.data.length} missing` : "Missing parcels"}
          </h3>
          {missing.isLoading && <Skeleton className="h-16 w-full" />}
          {missing.isError && (
            <ErrorState title="Couldn't load missing parcels" description="Reconciliation is unavailable right now." onRetry={() => missing.refetch()} />
          )}
          {missing.data && missing.data.length === 0 && <p className="text-sm text-muted-foreground">No parcels missing.</p>}
          {missing.data && missing.data.length > 0 && (
            <ul className="divide-y divide-border">
              {missing.data.map((p) => (
                <li key={p.shipmentId} className="flex items-center justify-between gap-3 py-2">
                  <div className="min-w-0">
                    <p className="font-mono text-sm">{parcelLabel(p)}</p>
                    <p className="text-xs text-muted-foreground">{`Scanned onto this manifest ${timeFormat.format(new Date(p.scannedAt))}`}</p>
                  </div>
                  {p.awb && (
                    <Button
                      type="button"
                      variant="link"
                      size="sm"
                      className="h-auto shrink-0 p-0"
                      aria-label={`Open parcel ${p.awb}`}
                      onClick={() => openShipment(p.awb)}
                    >
                      Open parcel
                    </Button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      </SectionCard>

      <SectionCard title="Parcels in this manifest" icon={Package}>
        {parcels.isLoading && <Skeleton className="h-24 w-full" />}
        {parcels.isError && (
          <ErrorState title="Couldn't load the parcel list" description="Try again in a moment." onRetry={() => parcels.refetch()} />
        )}
        {parcels.data && parcels.data.length === 0 && <p className="text-sm text-muted-foreground">No parcels on this manifest.</p>}
        {parcels.data && parcels.data.length > 0 && (
          <Table>
            <TableHeader>
              <TableRow className="hover:bg-transparent">
                <TableHead className="text-2xs">AWB</TableHead>
                <TableHead className="text-2xs">Scanned on</TableHead>
                <TableHead className="text-2xs">Received</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {parcels.data.map((p) => (
                <TableRow key={p.id}>
                  <TableCell className="font-mono text-sm">{parcelLabel(p)}</TableCell>
                  <TableCell className="text-sm text-muted-foreground">{timeFormat.format(new Date(p.scannedAt))}</TableCell>
                  <TableCell className="text-sm">{p.receivedAt ? timeFormat.format(new Date(p.receivedAt)) : "Not yet"}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </SectionCard>
    </div>
  );
}
