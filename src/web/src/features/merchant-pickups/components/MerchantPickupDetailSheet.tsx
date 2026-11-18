"use client";

import { useState } from "react";
import { Check } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetTitle } from "@/components/ui/sheet";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { ErrorState } from "@/components/states/ErrorState";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { FieldError } from "@/components/ui/field-error";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { cn } from "@/lib/utils";
import { formatDate, formatRs } from "@/features/cod/lib/codFormat";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";
import { useMerchantShipmentPanelStore } from "@/features/merchant-shipments/store/merchantShipmentPanelStore";
import { useMerchantPickups } from "@/features/merchant-pickups/hooks/useMerchantPickups";
import { useCancelMerchantPickup } from "@/features/merchant-pickups/hooks/useCancelMerchantPickup";
import { useMerchantPickupPanelStore } from "@/features/merchant-pickups/store/merchantPickupPanelStore";
import { pickupDayLabel } from "@/features/merchant-pickups/lib/pickupView";
import { pickupErrorMessage } from "@/features/merchant-pickups/lib/pickupErrorMessage";
import type { PickupStage } from "@/features/merchant-pickups/types";

const STEPS: PickupStage[] = ["Requested", "Scheduled", "Collected"];

export function MerchantPickupDetailSheet() {
  const panel = useMerchantPickupPanelStore((s) => s.panel);
  const close = useMerchantPickupPanelStore((s) => s.close);
  const openShipment = useMerchantShipmentPanelStore((s) => s.openDetail);
  const isOpen = panel.mode === "detail";
  const id = panel.mode === "detail" ? panel.id : "";

  const { data: pickups, isLoading, isError, refetch } = useMerchantPickups({ enabled: isOpen });
  const pickup = pickups?.find((p) => p.id === id);
  const cancelPickup = useCancelMerchantPickup();
  const canCancel = useMerchantPermission("merchant.pickups.cancel");
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [cancelError, setCancelError] = useState<string | null>(null);

  // requestedForDate is a plain yyyy-MM-dd; anchor at local noon so formatting never shifts the day.
  const dateLabel = pickup ? formatDate(`${pickup.requestedForDate}T12:00:00`) : "";

  async function handleCancel() {
    if (!pickup) return;
    setCancelError(null);
    try {
      await cancelPickup.mutateAsync(pickup.id);
    } catch (error) {
      setCancelError(pickupErrorMessage(error));
    }
  }

  function handleOpenChange(open: boolean) {
    if (!open) {
      setCancelError(null);
      close();
    }
  }

  const reached = pickup ? STEPS.indexOf(pickup.stage) : -1;
  const count = pickup?.shipments.length ?? 0;

  return (
    <Sheet open={isOpen} onOpenChange={handleOpenChange}>
      <SheetContent className="flex w-full flex-col gap-0 overflow-y-auto sm:max-w-xl">
        {!pickup && <SheetTitle className="sr-only">Pickup</SheetTitle>}
        {isLoading && <DetailSkeleton fieldRows={4} />}
        {isError && (
          <ErrorState title="Couldn't load this pickup" description="Something went wrong fetching it." onRetry={() => refetch()} />
        )}
        {!isLoading && !isError && isOpen && !pickup && (
          <ErrorState title="Pickup not found" description="It may have been removed." />
        )}
        {pickup && (
          <>
            <SheetHeaderBlock
              eyebrow={`PICKUP · REQUESTED ${formatDate(pickup.createdAt).toUpperCase()}`}
              title={pickupDayLabel(pickup.requestedForDate)}
              status={<StatusBadge status={pickup.stage} namespace="PickupStage" />}
              subtitle={`${pickup.branchName ?? "The branch"} collects ${count} ${count === 1 ? "parcel" : "parcels"}`}
            />

            <div className="space-y-6 py-4">
              {pickup.stage === "Cancelled" ? (
                <p className="text-sm font-medium text-muted-foreground">Cancelled</p>
              ) : (
                <ol aria-label="Pickup progress" className="flex gap-1">
                  {STEPS.map((step, i) => (
                    <li key={step} aria-current={i === reached ? "step" : undefined} className="flex-1 space-y-1.5">
                      <span className={cn("block h-1 rounded-full", i <= reached ? "bg-primary" : "bg-border")} />
                      <span
                        className={cn(
                          "flex items-center gap-1 text-xs",
                          i <= reached ? "font-medium text-foreground" : "text-muted-foreground"
                        )}
                      >
                        {i < reached && <Check className="h-3 w-3" strokeWidth={2} aria-hidden />}
                        {step}
                      </span>
                    </li>
                  ))}
                </ol>
              )}

              {pickup.notes && (
                <section aria-label="Your note to the rider" className="space-y-1 rounded-lg border border-border p-3">
                  <h3 className="text-2xs font-medium text-muted-foreground">Your note to the rider</h3>
                  <p className="text-sm text-foreground">{pickup.notes}</p>
                </section>
              )}

              <section aria-label="Parcels in this pickup" className="space-y-2">
                <h3 className="text-xs font-medium text-muted-foreground">Parcels</h3>
                {count === 0 ? (
                  <p className="text-sm text-muted-foreground">No parcels are attached to this pickup.</p>
                ) : (
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>AWB</TableHead>
                        <TableHead>Destination</TableHead>
                        <TableHead className="text-right">COD</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {pickup.shipments.map((s) => (
                        <TableRow key={s.id}>
                          <TableCell>
                            <Button
                              type="button"
                              variant="link"
                              className="h-auto p-0 font-mono"
                              onClick={() => openShipment(s.awb)}
                            >
                              {s.awb}
                            </Button>
                          </TableCell>
                          <TableCell>{s.destinationArea ?? "—"}</TableCell>
                          <TableCell className="text-right tabular-nums">
                            {s.codAmount === null ? "Prepaid" : formatRs(s.codAmount)}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                )}
              </section>
            </div>

            <div className="mt-auto space-y-2 border-t border-border pt-4">
              {canCancel && pickup.stage === "Requested" && (
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <p className="text-xs text-muted-foreground">You can cancel until the branch schedules a rider</p>
                  <Button
                    type="button"
                    variant="outline"
                    className="border-destructive/40 text-destructive hover:bg-destructive/5 hover:text-destructive"
                    onClick={() => setConfirmOpen(true)}
                    disabled={cancelPickup.isPending}
                  >
                    {cancelPickup.isPending ? <Spinner className="h-4 w-4" /> : "Cancel pickup"}
                  </Button>
                </div>
              )}
              {cancelError && <FieldError id="merchant-pickup-cancel-error">{cancelError}</FieldError>}
            </div>

            <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Cancel the pickup on {dateLabel}?</AlertDialogTitle>
                  <AlertDialogDescription>The branch won&apos;t send a rider.</AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>Keep pickup</AlertDialogCancel>
                  <AlertDialogAction onClick={handleCancel}>Cancel pickup</AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </>
        )}
      </SheetContent>
    </Sheet>
  );
}
