"use client";

import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { EmptyState } from "@/components/states/EmptyState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";
import { useMerchantPickups } from "@/features/merchant-pickups/hooks/useMerchantPickups";
import { useCancelMerchantPickup } from "@/features/merchant-pickups/hooks/useCancelMerchantPickup";
import { useMerchantPickupPanelStore } from "@/features/merchant-pickups/store/merchantPickupPanelStore";

export default function MerchantPickupsPage() {
  const { data: pickups, isLoading, isError, refetch } = useMerchantPickups();
  const canCreate = useMerchantPermission("merchant.pickups.create");
  const canCancel = useMerchantPermission("merchant.pickups.cancel");
  const openCreate = useMerchantPickupPanelStore((s) => s.openCreate);
  const cancelPickup = useCancelMerchantPickup();

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (isError) {
    return (
      <ErrorState
        title="Couldn't load pickup requests"
        description="Something went wrong fetching your pickup requests."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Pickups</h1>
        {canCreate && (
          <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
            <Plus className="h-4 w-4" strokeWidth={2} />
            Request Pickup
          </Button>
        )}
      </div>
      {!pickups || pickups.length === 0 ? (
        <EmptyState title="No pickup requests yet" description="Request a pickup once you have shipments ready." />
      ) : (
        <div className="overflow-hidden rounded-xl border border-border bg-muted/40">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Shipments</TableHead>
                <TableHead>Requested Date</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Notes</TableHead>
                {canCancel && <TableHead />}
              </TableRow>
            </TableHeader>
            <TableBody>
              {pickups.map((p) => (
                <TableRow key={p.id}>
                  <TableCell>{p.shipmentIds.length} shipment{p.shipmentIds.length === 1 ? "" : "s"}</TableCell>
                  <TableCell>{p.requestedForDate}</TableCell>
                  <TableCell><StatusBadge status={p.status} namespace="PickupRequest" /></TableCell>
                  <TableCell>{p.notes ?? "—"}</TableCell>
                  {canCancel && (
                    <TableCell>
                      {p.status === "Requested" && (
                        <Button
                          variant="ghost"
                          size="sm"
                          disabled={cancelPickup.isPending}
                          onClick={() => cancelPickup.mutate(p.id)}
                        >
                          Cancel
                        </Button>
                      )}
                    </TableCell>
                  )}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}
