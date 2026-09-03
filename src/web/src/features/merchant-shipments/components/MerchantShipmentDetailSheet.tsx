"use client";

import { useState } from "react";
import { Spinner } from "@/components/ui/spinner";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { FieldError } from "@/components/ui/field-error";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useMerchantBranches } from "@/features/merchant-shipments/hooks/useMerchantBranches";
import { useMerchantServiceTypes } from "@/features/merchant-shipments/hooks/useMerchantServiceTypes";
import { useMerchantShipment } from "@/features/merchant-shipments/hooks/useMerchantShipment";
import { useCancelMerchantShipment } from "@/features/merchant-shipments/hooks/useCancelMerchantShipment";
import { useMerchantShipmentPanelStore } from "@/features/merchant-shipments/store/merchantShipmentPanelStore";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";

const currencyFormatter = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" });

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

// Fidelity matches the staff ShipmentDetailSheet exactly (current status + route/package/money/
// items fields) — this is also this phase's "tracking" screen. Neither ShipmentResponse nor any
// endpoint exposes a ShipmentEvent timeline today, so there is no richer event-by-event history
// to show here either; that gap affects the staff app too and is out of this phase's scope
// (recorded in docs/CLARIFICATIONS.md entry #19).
export function MerchantShipmentDetailSheet() {
  const panel = useMerchantShipmentPanelStore((s) => s.panel);
  const close = useMerchantShipmentPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const awb = panel.mode === "detail" ? panel.awb : "";

  const { data: shipment, isLoading, isError, refetch } = useMerchantShipment(awb);
  const { data: branches } = useMerchantBranches();
  const { data: serviceTypes } = useMerchantServiceTypes();
  const cancelShipment = useCancelMerchantShipment();
  const canCancel = useMerchantPermission("shipment.cancel");
  const [cancelError, setCancelError] = useState<string | null>(null);

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

  const isTerminal = shipment?.currentStatus === "Delivered" || shipment?.currentStatus === "Cancelled";

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{shipment?.awb ?? "Shipment"}</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && (
            <ErrorState
              title="Couldn't load this shipment"
              description="It may not exist, or you may not have access to it."
              onRetry={() => refetch()}
            />
          )}
          {shipment && (
            <>
              <div className="grid grid-cols-2 gap-4">
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-muted-foreground">Status</span>
                  <StatusBadge status={shipment.currentStatus} />
                </div>
                <Field label="Service Type" value={serviceTypeName(shipment.serviceTypeId)} />
                <Field label="Created At" value={new Date(shipment.createdAt).toLocaleString()} />
              </div>

              <div className="grid grid-cols-2 gap-4">
                <Field label="Origin Branch" value={branchName(shipment.originBranchId)} />
                <Field label="Destination Branch" value={branchName(shipment.destinationBranchId)} />
                <Field label="Weight" value={`${shipment.weightKg} kg`} />
                <Field
                  label="Dimensions"
                  value={
                    shipment.lengthCm && shipment.widthCm && shipment.heightCm
                      ? `${shipment.lengthCm} x ${shipment.widthCm} x ${shipment.heightCm} cm`
                      : "—"
                  }
                />
              </div>

              <div className="grid grid-cols-2 gap-4">
                <Field label="Recipient" value={`${shipment.recipientName} (${shipment.recipientPhone})`} />
                <Field label="Recipient Address" value={shipment.recipientAddressLine} />
                <Field label="Sender" value={`${shipment.senderName} (${shipment.senderPhone})`} />
                <Field label="Sender Address" value={shipment.senderAddressLine} />
              </div>

              <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
                <Field label="Declared Value" value={currencyFormatter.format(shipment.declaredValue)} />
                <Field label="COD Amount" value={currencyFormatter.format(shipment.codAmount)} />
                <Field label="Delivery Charge" value={currencyFormatter.format(shipment.deliveryCharge)} />
                <Field label="Insurance Charge" value={currencyFormatter.format(shipment.insuranceCharge)} />
                <Field label="Other Charges" value={currencyFormatter.format(shipment.otherCharges)} />
                <Field label="Total Charge" value={currencyFormatter.format(shipment.totalCharge)} />
              </div>

              <div className="space-y-2">
                <span className="text-xs text-muted-foreground">Items</span>
                <ul className="space-y-1">
                  {shipment.items.map((item) => (
                    <li key={item.id} className="flex justify-between text-sm">
                      <span>{item.description} × {item.quantity}</span>
                      <span>{currencyFormatter.format(item.unitValue)}</span>
                    </li>
                  ))}
                </ul>
              </div>

              {canCancel && !isTerminal && (
                <div className="space-y-2 border-t border-border pt-4">
                  <Button
                    type="button"
                    variant="destructive"
                    onClick={handleCancel}
                    disabled={cancelShipment.isPending}
                  >
                    {cancelShipment.isPending ? <Spinner className="h-4 w-4" /> : "Cancel Shipment"}
                  </Button>
                  {cancelError && <FieldError id="merchant-cancel-error">{cancelError}</FieldError>}
                </div>
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
