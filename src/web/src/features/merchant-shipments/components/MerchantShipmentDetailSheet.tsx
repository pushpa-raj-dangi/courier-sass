"use client";

import { useEffect, useState, type ReactNode } from "react";
import { Spinner } from "@/components/ui/spinner";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent } from "@/components/ui/sheet";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
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
import { SectionError } from "@/components/states/SectionError";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { FieldError } from "@/components/ui/field-error";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { cn } from "@/lib/utils";
import { formatRs } from "@/features/cod/lib/codFormat";
import { useMerchantBranches } from "@/features/merchant-shipments/hooks/useMerchantBranches";
import { useMerchantServiceTypes } from "@/features/merchant-shipments/hooks/useMerchantServiceTypes";
import { useMerchantShipment } from "@/features/merchant-shipments/hooks/useMerchantShipment";
import { useCancelMerchantShipment } from "@/features/merchant-shipments/hooks/useCancelMerchantShipment";
import { useMerchantShipmentPanelStore } from "@/features/merchant-shipments/store/merchantShipmentPanelStore";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";
import { useMerchantShipmentCodStatus } from "@/features/merchant-cod/hooks/useMerchantShipmentCodStatus";
import { useTrackShipment } from "@/features/tracking/hooks/useTrackShipment";
import { buildJourney, formatEventTime } from "@/features/tracking/lib/journey";
import { TrackingJourney } from "@/features/tracking/components/TrackingJourney";
import { codLine } from "@/features/merchant-shipments/lib/codLine";
import { groupOf } from "@/features/merchant-shipments/lib/statusGroups";

function Row({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-4 py-1.5">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-right text-sm font-medium text-foreground">{value}</dd>
    </div>
  );
}

export function MerchantShipmentDetailSheet() {
  const panel = useMerchantShipmentPanelStore((s) => s.panel);
  const close = useMerchantShipmentPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const awb = panel.mode === "detail" ? panel.awb : "";

  const { data: shipment, isLoading, isError, refetch } = useMerchantShipment(awb);
  const { data: branches } = useMerchantBranches();
  const { data: serviceTypes } = useMerchantServiceTypes();
  const { data: codStatus } = useMerchantShipmentCodStatus(shipment?.id);
  const tracking = useTrackShipment(awb, { poll: false });
  const cancelShipment = useCancelMerchantShipment();
  const canCancel = useMerchantPermission("shipment.cancel");
  const [cancelError, setCancelError] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) return;
    const t = setTimeout(() => setCopied(false), 2500);
    return () => clearTimeout(t);
  }, [copied]);

  const branchName = (id: string) => branches?.find((b) => b.id === id)?.name ?? "—";
  const serviceTypeName = (id: string) => serviceTypes?.find((s) => s.id === id)?.name ?? "—";

  async function handleCancel() {
    if (!shipment) return;
    setCancelError(null);
    try {
      await cancelShipment.mutateAsync(shipment.id);
    } catch {
      setCancelError("Couldn't cancel this shipment. It may already be past a cancellable stage.");
    }
  }

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(`${window.location.origin}/track/${awb}`);
      setCopied(true);
    } catch {
      // Clipboard can be unavailable (insecure context); nothing useful to show.
    }
  }

  const journey = tracking.data ? buildJourney(tracking.data) : null;
  const doneCount = journey ? journey.filter((s) => s.state === "done").length : 0;
  const isPrepaid = shipment ? shipment.codAmount === 0 : false;
  const cancellable = shipment ? groupOf(shipment.currentStatus) === "booked" : false;

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="flex w-full flex-col gap-0 overflow-y-auto sm:max-w-xl">
        {isLoading && <DetailSkeleton fieldRows={4} />}
        {isError && (
          <ErrorState
            title="Couldn't load this shipment"
            description="It may not exist, or you may not have access to it."
            onRetry={() => refetch()}
          />
        )}
        {shipment && (
          <>
            <SheetHeaderBlock
              eyebrow={`AWB · ${serviceTypeName(shipment.serviceTypeId).toUpperCase()} · ${isPrepaid ? "PREPAID" : "COD"}`}
              title={shipment.awb}
              mono
              status={<StatusBadge status={shipment.currentStatus} namespace="Merchant" />}
              facts={[
                { label: "COD to collect", value: isPrepaid ? "Prepaid" : formatRs(shipment.codAmount) },
                { label: "Your charge", value: formatRs(shipment.totalCharge) },
                ...(isPrepaid ? [] : [{ label: "You receive", value: formatRs(shipment.codAmount - shipment.totalCharge) }]),
                { label: "Weight", value: `${shipment.weightKg} kg` },
              ]}
            />

            <div className="space-y-6 py-4">
              <section aria-label="Where it is now">
                <h3 className="mb-1 text-xs font-medium text-muted-foreground">Where it is now</h3>
                <dl className="divide-y divide-border">
                  <Row label="Status" value={<StatusBadge status={shipment.currentStatus} namespace="Merchant" />} />
                  <Row label="Delivering branch" value={branchName(shipment.destinationBranchId)} />
                  <Row label="Last update" value={shipment.lastEventAt ? formatEventTime(shipment.lastEventAt) : "—"} />
                  <Row label="COD settlement" value={codLine(codStatus, shipment.codAmount)} />
                </dl>
              </section>

              {journey && (
                <div
                  role="img"
                  aria-label={`Progress: ${doneCount} of ${journey.length} steps`}
                  className="flex gap-1"
                >
                  {journey.map((step) => (
                    <span
                      key={step.stage}
                      className={cn("h-1 flex-1 rounded-full", step.state === "next" ? "bg-border" : "bg-primary")}
                    />
                  ))}
                </div>
              )}

              <Tabs defaultValue="journey">
                <TabsList>
                  <TabsTrigger value="journey">Journey</TabsTrigger>
                  <TabsTrigger value="parcel">Parcel</TabsTrigger>
                  <TabsTrigger value="recipient">Recipient</TabsTrigger>
                  <TabsTrigger value="charges">Charges</TabsTrigger>
                </TabsList>

                <TabsContent value="journey">
                  {tracking.isLoading && <Skeleton className="mt-6 h-32 w-full" />}
                  {tracking.isError && (
                    <div className="mt-6">
                      <SectionError message="Journey unavailable right now" onRetry={() => tracking.refetch()} />
                    </div>
                  )}
                  {journey && <TrackingJourney steps={journey} />}
                </TabsContent>

                <TabsContent value="parcel">
                  <dl className="mt-4 divide-y divide-border">
                    <Row label="Service" value={serviceTypeName(shipment.serviceTypeId)} />
                    <Row label="Origin" value={branchName(shipment.originBranchId)} />
                    <Row label="Destination" value={branchName(shipment.destinationBranchId)} />
                    <Row label="Weight" value={`${shipment.weightKg} kg`} />
                    <Row
                      label="Dimensions"
                      value={
                        shipment.lengthCm && shipment.widthCm && shipment.heightCm
                          ? `${shipment.lengthCm} x ${shipment.widthCm} x ${shipment.heightCm} cm`
                          : "—"
                      }
                    />
                    <Row label="Declared value" value={formatRs(shipment.declaredValue)} />
                    <Row label="Booked" value={formatEventTime(shipment.createdAt)} />
                  </dl>
                  {shipment.items.length > 0 && (
                    <ul className="mt-4 space-y-1">
                      {shipment.items.map((item) => (
                        <li key={item.id} className="flex justify-between text-sm">
                          <span>{item.description} × {item.quantity}</span>
                          <span>{formatRs(item.unitValue)}</span>
                        </li>
                      ))}
                    </ul>
                  )}
                </TabsContent>

                <TabsContent value="recipient">
                  <dl className="mt-4 divide-y divide-border">
                    <Row label="Recipient" value={shipment.recipientName} />
                    <Row label="Phone" value={shipment.recipientPhone} />
                    <Row label="Address" value={shipment.recipientAddressLine} />
                    <Row label="Sender" value={`${shipment.senderName} (${shipment.senderPhone})`} />
                    <Row label="Pickup address" value={shipment.senderAddressLine} />
                  </dl>
                </TabsContent>

                <TabsContent value="charges">
                  <dl className="mt-4 divide-y divide-border">
                    <Row label="Delivery charge" value={formatRs(shipment.deliveryCharge)} />
                    <Row label="Insurance" value={formatRs(shipment.insuranceCharge)} />
                    <Row label="Other charges" value={formatRs(shipment.otherCharges)} />
                    <Row label="Total charge" value={formatRs(shipment.totalCharge)} />
                    {!isPrepaid && <Row label="COD amount" value={formatRs(shipment.codAmount)} />}
                  </dl>
                </TabsContent>
              </Tabs>
            </div>

            <div className="mt-auto space-y-2 border-t border-border pt-4">
              <div className="flex flex-wrap items-center gap-2">
                {canCancel && cancellable && (
                  <Button
                    type="button"
                    variant="destructive"
                    onClick={() => setConfirmOpen(true)}
                    disabled={cancelShipment.isPending}
                  >
                    {cancelShipment.isPending ? <Spinner className="h-4 w-4" /> : "Cancel shipment"}
                  </Button>
                )}
                <Button type="button" variant="outline" onClick={handleCopy}>
                  Copy tracking link
                </Button>
                <span role="status" className="text-xs text-muted-foreground">
                  {copied ? "Tracking link copied" : ""}
                </span>
              </div>
              {cancelError && <FieldError id="merchant-cancel-error">{cancelError}</FieldError>}
            </div>

            <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Cancel this shipment? This can&apos;t be undone.</AlertDialogTitle>
                  <AlertDialogDescription>
                    {shipment.awb} will be cancelled and will no longer be picked up.
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>Keep shipment</AlertDialogCancel>
                  <AlertDialogAction onClick={handleCancel}>Cancel shipment</AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </>
        )}
      </SheetContent>
    </Sheet>
  );
}
