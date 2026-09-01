"use client";

import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useReturnShipment } from "@/features/ndr/hooks/useReturnShipment";
import { useConfirmReturnShipment } from "@/features/ndr/hooks/useConfirmReturnShipment";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function ReturnShipmentDetailSheet() {
  const panel = useReturnShipmentPanelStore((s) => s.panel);
  const close = useReturnShipmentPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const id = panel.mode === "detail" ? panel.id : "";

  const { data: returnShipment, isLoading, isError, refetch } = useReturnShipment(id);
  const { data: branches } = useBranches();
  const confirmReturn = useConfirmReturnShipment(id);
  const canManage = usePermission("rto.manage");
  const branchName = (branchId: string) => branches?.find((b) => b.id === branchId)?.name ?? "—";

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{returnShipment?.returnAwb ?? "Return Shipment"}</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && (
            <ErrorState
              title="Couldn't load this return shipment"
              description="It may not exist, or you may not have access to it."
              onRetry={() => refetch()}
            />
          )}
          {returnShipment && (
            <>
              <div className="grid grid-cols-2 gap-4">
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-muted-foreground">Status</span>
                  <StatusBadge status={returnShipment.currentStatus} />
                </div>
                <Field label="Original AWB" value={returnShipment.originalAwb} />
                <Field label="Origin Branch" value={branchName(returnShipment.originBranchId)} />
                <Field label="Destination Branch" value={branchName(returnShipment.destinationBranchId)} />
                <Field label="RTO Reason" value={returnShipment.rtoReason} />
                <Field
                  label="Merchant Confirmed"
                  value={returnShipment.merchantConfirmedAt ? new Date(returnShipment.merchantConfirmedAt).toLocaleString() : "Not yet"}
                />
              </div>

              {canManage && !returnShipment.merchantConfirmedAt && (
                <div className="space-y-2">
                  {returnShipment.currentStatus !== "RtoAtOrigin" && (
                    <p className="text-sm text-muted-foreground">
                      Return must reach the origin branch before it can be confirmed, currently {returnShipment.currentStatus}.
                    </p>
                  )}
                  <Button
                    type="button"
                    disabled={returnShipment.currentStatus !== "RtoAtOrigin" || confirmReturn.isPending}
                    onClick={() => confirmReturn.mutate()}
                  >
                    {confirmReturn.isPending ? <Spinner className="h-4 w-4" /> : "Confirm Return"}
                  </Button>
                </div>
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
