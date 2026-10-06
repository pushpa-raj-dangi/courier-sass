"use client";

import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetTitle } from "@/components/ui/sheet";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { ErrorState } from "@/components/states/ErrorState";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { isNotFoundError } from "@/lib/api/clientFetch";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { scanErrorMessage } from "@/features/scanning/scanErrors";
import { useShipment } from "@/features/shipments/hooks/useShipment";
import { useShipmentEvents } from "@/features/shipments/hooks/useShipmentEvents";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { useReceiveAtOrigin } from "@/features/shipments/hooks/useReceiveAtOrigin";
import { useServiceTypes } from "@/features/shipments/hooks/useServiceTypes";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import { ShipmentNdrRtoLinks } from "@/features/shipments/components/ShipmentNdrRtoLinks";
import { custodySince } from "@/features/shipments/lib/custodyChain";
import { ProgressStepper } from "./detail/ProgressStepper";
import { RightNowCard } from "./detail/RightNowCard";
import { TimelineTab } from "./detail/TimelineTab";
import { CustodyTab } from "./detail/CustodyTab";
import { PartiesTab } from "./detail/PartiesTab";

const rs = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });

export function ShipmentDetailSheet() {
  const panel = useShipmentPanelStore((s) => s.panel);
  const close = useShipmentPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const awb = panel.mode === "detail" ? panel.awb : "";

  const { data: shipment, isLoading, isError, error, refetch } = useShipment(awb);
  const events = useShipmentEvents(awb);
  const { data: merchants } = useMerchants();
  const { data: serviceTypes } = useServiceTypes();
  const canScan = usePermission("shipment.scan");
  const receiveAtOrigin = useReceiveAtOrigin(shipment?.id ?? "");

  const serviceName = shipment ? serviceTypes?.find((s) => s.id === shipment.serviceTypeId)?.name ?? "Service" : "";
  const merchantName = shipment ? merchants?.find((m) => m.id === shipment.merchantId)?.displayName ?? "—" : "—";

  const historyError = (
    <ErrorState title="Couldn't load the history" description="The shipment's events could not be fetched." onRetry={() => events.refetch()} />
  );

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="flex w-full flex-col gap-0 p-0 sm:max-w-[580px]">
        <div className="flex-1 space-y-5 overflow-y-auto p-6">
          {!shipment && <SheetTitle className="sr-only">Shipment</SheetTitle>}
          {isLoading && <DetailSkeleton fieldRows={5} />}
          {isError && (isNotFoundError(error) ? (
            <ErrorState
              title="Shipment not found or not in your branches"
              description="Check the AWB, or ask an admin for access to the branch that holds it."
              onRetry={() => refetch()}
            />
          ) : (
            <ErrorState
              title="Couldn't load shipment"
              description="Something went wrong fetching this shipment. Check your connection and try again."
              onRetry={() => refetch()}
            />
          ))}
          {shipment && (
            <>
              <SheetHeaderBlock
                eyebrow={`AWB · ${serviceName.toUpperCase()} · ${shipment.codAmount > 0 ? "COD" : "PREPAID"}`}
                title={shipment.awb}
                mono
                status={<StatusBadge status={shipment.currentStatus} />}
                facts={[
                  { label: "Merchant", value: merchantName },
                  { label: "COD", value: shipment.codAmount > 0 ? `Rs ${rs.format(shipment.codAmount)}` : "—" },
                  { label: "Weight", value: `${shipment.weightKg} kg` },
                  { label: "Charge", value: `Rs ${rs.format(shipment.totalCharge)}` },
                ]}
              />
              <RightNowCard shipment={shipment} since={events.data ? custodySince(events.data) : null} />
              <ProgressStepper status={shipment.currentStatus} />
              {(shipment.currentStatus === "NdrCreated" || shipment.currentStatus.startsWith("Rto")) && (
                <ShipmentNdrRtoLinks shipmentId={shipment.id} />
              )}
              <Tabs defaultValue="timeline">
                <TabsList className="w-full justify-start overflow-x-auto">
                  <TabsTrigger value="timeline">Timeline</TabsTrigger>
                  <TabsTrigger value="custody">Custody chain</TabsTrigger>
                  <TabsTrigger value="cod">COD</TabsTrigger>
                  <TabsTrigger value="attempts">Attempts</TabsTrigger>
                  <TabsTrigger value="parties">Parties</TabsTrigger>
                  <TabsTrigger value="audit">Audit</TabsTrigger>
                </TabsList>
                <TabsContent value="timeline" className="pt-4">
                  {events.isLoading ? <DetailSkeleton fieldRows={4} /> : events.isError ? historyError : <TimelineTab events={events.data ?? []} />}
                </TabsContent>
                <TabsContent value="custody" className="pt-4">
                  {events.isLoading ? <DetailSkeleton fieldRows={3} /> : events.isError ? historyError : <CustodyTab events={events.data ?? []} />}
                </TabsContent>
                <TabsContent value="cod" className="pt-4">
                  <ComingSoonSlot title="COD for this shipment" description="Collection, rider deposit and settlement status for this parcel." />
                </TabsContent>
                <TabsContent value="attempts" className="pt-4">
                  <ComingSoonSlot title="Delivery attempts" description="Each attempt with reason, rider, time and next action." />
                </TabsContent>
                <TabsContent value="parties" className="pt-4">
                  <PartiesTab shipment={shipment} />
                </TabsContent>
                <TabsContent value="audit" className="pt-4">
                  <ComingSoonSlot title="Audit trail" description="Edits and corrections to this shipment with before/after values." />
                </TabsContent>
              </Tabs>
            </>
          )}
        </div>
        {shipment && (
          <div className="flex items-center gap-2.5 border-t border-border bg-surface-sunken px-6 py-3.5">
            {receiveAtOrigin.isError ? (
              <p role="alert" className="flex-1 text-[11.5px] text-destructive">
                Couldn't receive at origin: {scanErrorMessage(receiveAtOrigin.error)}
              </p>
            ) : (
              <span className="flex-1 text-[11.5px] text-muted-foreground">Status changes only through scans and rider actions</span>
            )}
            {canScan && shipment.currentStatus === "PickedUp" && (
              <Button size="sm" disabled={receiveAtOrigin.isPending} onClick={() => receiveAtOrigin.mutate()}>
                {receiveAtOrigin.isPending ? "Receiving…" : "Receive at origin"}
              </Button>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
