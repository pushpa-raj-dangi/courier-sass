"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { FieldError } from "@/components/ui/field-error";
import { ErrorState } from "@/components/states/ErrorState";
import { useAdjustments } from "@/features/cod/hooks/useAdjustments";
import { useApproveAdjustment } from "@/features/cod/hooks/useApproveAdjustment";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { codErrorMessage } from "@/features/cod/lib/codErrorMessage";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function AdjustmentDetailSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const isOpen = panel.mode === "adjustment-detail";
  const id = panel.mode === "adjustment-detail" ? panel.id : "";

  const [serverError, setServerError] = useState<string | null>(null);
  const canAdjust = usePermission("cod.adjust");

  // No GET-by-id endpoint exists for a single Adjustment (only the list, same shape as
  // RiderCashSettlement/BranchCashSettlement) — AdjustmentResponse has no field the list shape
  // omits, so finding the row in a cached, generously-paged list is sufficient.
  const { data, isLoading, isError, refetch } = useAdjustments({ pageSize: 200 });
  const adjustment = data?.items.find((a) => a.id === id);
  const approveMutation = useApproveAdjustment(id);

  async function handleApprove() {
    setServerError(null);
    try {
      await approveMutation.mutateAsync();
    } catch (error) {
      setServerError(codErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) { setServerError(null); close(); } }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-lg">
        <SheetHeader>
          <SheetTitle>Adjustment</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && <ErrorState title="Couldn't load this adjustment" description="Something went wrong." onRetry={() => refetch()} />}
          {!isLoading && !isError && !adjustment && (
            <ErrorState title="Adjustment not found" description="It may be outside the current page." onRetry={() => refetch()} />
          )}
          {adjustment && (
            <>
              <div className="grid grid-cols-2 gap-4">
                <Field label="Entity Type" value={adjustment.entityType} />
                <Field label="Before Value" value={`Rs. ${adjustment.beforeValue.toFixed(2)}`} />
                <Field label="After Value" value={`Rs. ${adjustment.afterValue.toFixed(2)}`} />
                <Field label="Requested By" value={adjustment.requestedByUserId} />
                <Field label="Requested At" value={new Date(adjustment.requestedAt).toLocaleString()} />
              </div>
              <div className="space-y-1">
                <span className="text-xs text-muted-foreground">Reason</span>
                <p className="rounded-md border border-border p-3 text-sm text-foreground">{adjustment.reason}</p>
              </div>
              {adjustment.applied ? (
                <p className="text-sm text-muted-foreground">
                  Approved {adjustment.approvedAt ? new Date(adjustment.approvedAt).toLocaleString() : ""}.
                </p>
              ) : (
                canAdjust && (
                  <div className="space-y-2">
                    {serverError && <FieldError id="adjustment-server-error">{serverError}</FieldError>}
                    <Button type="button" disabled={approveMutation.isPending} onClick={handleApprove}>
                      {approveMutation.isPending ? <Spinner className="h-4 w-4" /> : "Approve"}
                    </Button>
                  </div>
                )
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
