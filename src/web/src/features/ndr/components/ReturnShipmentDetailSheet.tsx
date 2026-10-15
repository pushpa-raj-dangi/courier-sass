"use client";

import { ArrowRight, History, PackageCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetTitle } from "@/components/ui/sheet";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle, AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { SectionCard } from "@/components/page/SectionCard";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { ErrorState } from "@/components/states/ErrorState";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useShipment } from "@/features/shipments/hooks/useShipment";
import { useShipmentEvents } from "@/features/shipments/hooks/useShipmentEvents";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import { TimelineTab } from "@/features/shipments/components/detail/TimelineTab";
import { useReturnShipment } from "@/features/ndr/hooks/useReturnShipment";
import { useConfirmReturnShipment } from "@/features/ndr/hooks/useConfirmReturnShipment";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";
import { canHandBack } from "@/features/ndr/lib/returnModel";

const confirmedFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" });

function shortReason(reason: string): string {
  return reason.length > 40 ? `${reason.slice(0, 39)}…` : reason;
}

export function ReturnShipmentDetailSheet() {
  const panel = useReturnShipmentPanelStore((s) => s.panel);
  const close = useReturnShipmentPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const id = panel.mode === "detail" ? panel.id : "";

  const { data: returnShipment, isLoading, isError, refetch } = useReturnShipment(id);
  // The return lifecycle runs on the ORIGINAL shipment's status, so its staff events are the
  // return's story; its read also gives the merchant (ReturnShipment has no MerchantId).
  const originalAwb = returnShipment?.originalAwb ?? "";
  const original = useShipment(originalAwb);
  const events = useShipmentEvents(originalAwb);
  const { data: merchants } = useMerchants();
  const names = usePeopleNames();
  const canManage = usePermission("rto.manage");
  const confirm = useConfirmReturnShipment(id);
  const openShipment = useShipmentPanelStore((s) => s.openDetail);

  const merchantName = original.data ? merchants?.find((m) => m.id === original.data!.merchantId)?.displayName ?? "—" : "—";

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="flex w-full flex-col gap-6 overflow-y-auto sm:max-w-xl">
        {!returnShipment && <SheetTitle className="sr-only">Return shipment</SheetTitle>}
        {isLoading && <DetailSkeleton fieldRows={1} />}
        {isError && (
          <ErrorState title="Couldn't load this return" description="It may not exist, or you may not have access to it." onRetry={() => refetch()} />
        )}
        {returnShipment && (
          <>
            <SheetHeaderBlock
              eyebrow={`RETURN · ${shortReason(returnShipment.rtoReason)}`}
              title={returnShipment.returnAwb}
              mono
              status={<StatusBadge status={returnShipment.currentStatus} />}
              facts={[
                { label: "Back to", value: names.branchName(returnShipment.destinationBranchId) },
                { label: "Now at", value: returnShipment.currentBranchId ? names.branchName(returnShipment.currentBranchId) : "In transit" },
                { label: "Merchant", value: merchantName },
              ]}
            />

            <div className="grid grid-cols-[1fr_auto_1fr] items-center gap-2">
              <button
                type="button"
                disabled={!returnShipment.originalAwb}
                onClick={() => openShipment(returnShipment.originalAwb)}
                aria-label={`Open original shipment ${returnShipment.originalAwb}`}
                className="rounded-lg border border-border p-3 text-left transition-colors hover:bg-accent disabled:pointer-events-none"
              >
                <span className="block text-2xs text-muted-foreground">Original</span>
                <span className="block font-mono text-sm">{returnShipment.originalAwb || "—"}</span>
              </button>
              <ArrowRight className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
              <div className="rounded-lg border border-primary p-3">
                <span className="block text-2xs text-muted-foreground">Return</span>
                <span className="block font-mono text-sm">{returnShipment.returnAwb}</span>
              </div>
            </div>

            <SectionCard title="What happened" icon={History} description="Events on the original shipment, newest first.">
              {events.isLoading && <Skeleton className="h-24 w-full" />}
              {events.isError && (
                <ErrorState title="Couldn't load the parcel history" description="It needs shipment read access." onRetry={() => events.refetch()} />
              )}
              {events.data && <TimelineTab events={events.data} />}
            </SectionCard>

            {confirm.isSuccess && <p role="status" className="text-sm font-medium text-foreground">Handed back to the merchant.</p>}
            {returnShipment.merchantConfirmedAt ? (
              <p className="text-sm text-foreground">{`Confirmed by merchant ${confirmedFormat.format(new Date(returnShipment.merchantConfirmedAt))}`}</p>
            ) : canManage && canHandBack(returnShipment) ? (
              <SectionCard title="Hand back to merchant" icon={PackageCheck}>
                <div className="space-y-4">
                  <p className="text-sm text-muted-foreground">The parcel is at the origin branch. Confirm once the merchant has it back.</p>
                  <ComingSoonSlot title="Handover details" description="Received by and parcel condition are not recorded yet." />
                  <AlertDialog>
                    <AlertDialogTrigger asChild>
                      <Button type="button" disabled={confirm.isPending}>
                        {confirm.isPending ? <Spinner className="h-4 w-4" /> : "Confirm handover"}
                      </Button>
                    </AlertDialogTrigger>
                    <AlertDialogContent>
                      <AlertDialogHeader>
                        <AlertDialogTitle>{`Confirm handover of ${returnShipment.returnAwb}?`}</AlertDialogTitle>
                        <AlertDialogDescription>This marks the return delivered to the merchant. It can&apos;t be undone.</AlertDialogDescription>
                      </AlertDialogHeader>
                      <AlertDialogFooter>
                        <AlertDialogCancel>Not yet</AlertDialogCancel>
                        <AlertDialogAction onClick={() => confirm.mutate()}>Confirm handover</AlertDialogAction>
                      </AlertDialogFooter>
                    </AlertDialogContent>
                  </AlertDialog>
                  {confirm.isError && <p role="alert" className="text-sm text-destructive">Couldn&apos;t confirm the handover. Try again.</p>}
                </div>
              </SectionCard>
            ) : canManage ? (
              <p className="text-sm text-muted-foreground">Hand-back opens once the parcel is at the origin branch.</p>
            ) : null}
          </>
        )}
      </SheetContent>
    </Sheet>
  );
}
