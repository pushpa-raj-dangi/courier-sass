"use client";

import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useShipment } from "@/features/shipments/hooks/useShipment";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { useServiceTypes } from "@/features/shipments/hooks/useServiceTypes";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import { ShipmentNdrRtoLinks } from "@/features/shipments/components/ShipmentNdrRtoLinks";

const currencyFormatter = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" });

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function ShipmentDetailSheet() {
  const panel = useShipmentPanelStore((s) => s.panel);
  const close = useShipmentPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const awb = panel.mode === "detail" ? panel.awb : "";

  const { data: shipment, isLoading, isError, refetch } = useShipment(awb);
  const { data: branches } = useBranches();
  const { data: merchants } = useMerchants();
  const { data: serviceTypes } = useServiceTypes();

  const branchName = (id: string) => branches?.find((b) => b.id === id)?.name ?? "—";
  const merchantName = (id: string) => merchants?.find((m) => m.id === id)?.displayName ?? "—";
  const serviceTypeName = (id: string) => serviceTypes?.find((s) => s.id === id)?.name ?? "—";

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
                <Field label="Merchant" value={merchantName(shipment.merchantId)} />
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

              {(shipment.currentStatus === "NdrCreated" || shipment.currentStatus.startsWith("Rto")) && (
                <ShipmentNdrRtoLinks shipmentId={shipment.id} />
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
