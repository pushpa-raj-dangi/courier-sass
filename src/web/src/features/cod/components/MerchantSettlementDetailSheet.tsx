"use client";

import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { FieldError } from "@/components/ui/field-error";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useMerchantSettlement } from "@/features/cod/hooks/useMerchantSettlement";
import { useSettleMerchantSettlement } from "@/features/cod/hooks/useSettleMerchantSettlement";
import { useRequestAdjustment } from "@/features/cod/hooks/useRequestAdjustment";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { codErrorMessage } from "@/features/cod/lib/codErrorMessage";
import { requestAdjustmentSchema, type RequestAdjustmentFormValues } from "@/features/cod/schema";
import type { MerchantSettlementItem } from "@/features/cod/types";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

function AdjustmentForm({ item, onDone }: { item: MerchantSettlementItem; onDone: () => void }) {
  const requestAdjustment = useRequestAdjustment();
  const [serverError, setServerError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<RequestAdjustmentFormValues>({
    resolver: zodResolver(requestAdjustmentSchema),
    defaultValues: { afterValue: item.otherDeductions },
  });

  async function onSubmit(values: RequestAdjustmentFormValues) {
    setServerError(null);
    try {
      await requestAdjustment.mutateAsync({ entityType: "MerchantSettlementItem", entityId: item.id, ...values });
      onDone();
    } catch (error) {
      setServerError(codErrorMessage(error));
    }
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="mt-2 space-y-2 rounded-md border border-border p-3" noValidate>
      <div className="space-y-1">
        <Label htmlFor={`adjust-value-${item.id}`}>New Other Deductions</Label>
        <Input
          id={`adjust-value-${item.id}`}
          aria-label="New Other Deductions"
          type="number"
          step="0.01"
          aria-invalid={!!errors.afterValue}
          aria-describedby={errors.afterValue ? `adjust-value-${item.id}-error` : undefined}
          {...register("afterValue", { valueAsNumber: true })}
        />
        {errors.afterValue && (
          <FieldError id={`adjust-value-${item.id}-error`}>{errors.afterValue.message}</FieldError>
        )}
      </div>
      <div className="space-y-1">
        <Label htmlFor={`adjust-reason-${item.id}`}>Reason</Label>
        <Input
          id={`adjust-reason-${item.id}`}
          aria-label="Reason"
          aria-invalid={!!errors.reason}
          aria-describedby={errors.reason ? `adjust-reason-${item.id}-error` : undefined}
          {...register("reason")}
        />
        {errors.reason && <FieldError id={`adjust-reason-${item.id}-error`}>{errors.reason.message}</FieldError>}
      </div>
      {serverError && <FieldError id={`adjust-server-error-${item.id}`}>{serverError}</FieldError>}
      <div className="flex gap-2">
        <Button type="submit" size="sm" disabled={isSubmitting}>
          {isSubmitting ? <Spinner className="h-4 w-4" /> : "Request Adjustment"}
        </Button>
        <Button type="button" size="sm" variant="outline" onClick={onDone}>Cancel</Button>
      </div>
    </form>
  );
}

export function MerchantSettlementDetailSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const isOpen = panel.mode === "merchant-settlement-detail";
  const id = panel.mode === "merchant-settlement-detail" ? panel.id : "";

  const [adjustingItemId, setAdjustingItemId] = useState<string | null>(null);
  const [settleError, setSettleError] = useState<string | null>(null);
  const canAdjust = usePermission("cod.adjust");
  const canSettle = usePermission("cod.settle");

  const { data: settlement, isLoading, isError, refetch } = useMerchantSettlement(id);
  const settleMutation = useSettleMerchantSettlement(id);

  async function handleSettle() {
    setSettleError(null);
    try {
      await settleMutation.mutateAsync();
    } catch (error) {
      setSettleError(codErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) { setAdjustingItemId(null); close(); } }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-2xl">
        <SheetHeader>
          <SheetTitle>Merchant Settlement</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && <ErrorState title="Couldn't load this settlement" description="Something went wrong." onRetry={() => refetch()} />}
          {settlement && (
            <>
              <div className="grid grid-cols-2 gap-4">
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-muted-foreground">Status</span>
                  <StatusBadge status={settlement.status} namespace="MerchantSettlement" />
                </div>
                <Field label="Total Payable" value={`Rs. ${settlement.totalPayable.toFixed(2)}`} />
                <Field label="Period From" value={new Date(settlement.periodFrom).toLocaleDateString()} />
                <Field label="Period To" value={new Date(settlement.periodTo).toLocaleDateString()} />
                <Field label="Settled At" value={settlement.settledAt ? new Date(settlement.settledAt).toLocaleString() : "—"} />
              </div>

              <div className="space-y-2">
                <Label>Line Items</Label>
                <div className="space-y-2">
                  {settlement.items.map((item) => (
                    <div key={item.id} className="rounded-md border border-border p-3 text-sm">
                      <div className="flex items-center justify-between">
                        <span className="font-medium text-foreground">{item.shipmentAwb}</span>
                        {canAdjust && settlement.status === "Created" && adjustingItemId !== item.id && (
                          <Button type="button" size="sm" variant="outline" onClick={() => setAdjustingItemId(item.id)}>
                            Request Adjustment
                          </Button>
                        )}
                      </div>
                      <div className="mt-1 grid grid-cols-4 gap-2 text-muted-foreground">
                        <span>COD: Rs. {item.codAmount.toFixed(2)}</span>
                        <span>Fee: Rs. {item.courierFee.toFixed(2)}</span>
                        <span>Deductions: Rs. {item.otherDeductions.toFixed(2)}</span>
                        <span>Net: Rs. {item.netPayable.toFixed(2)}</span>
                      </div>
                      {adjustingItemId === item.id && (
                        <AdjustmentForm item={item} onDone={() => setAdjustingItemId(null)} />
                      )}
                    </div>
                  ))}
                </div>
              </div>

              {canSettle && (
                <div className="space-y-2">
                  {settleError && <FieldError id="settle-error">{settleError}</FieldError>}
                  <Button type="button" disabled={settlement.status !== "Created" || settleMutation.isPending} onClick={handleSettle}>
                    {settleMutation.isPending ? <Spinner className="h-4 w-4" /> : "Settle"}
                  </Button>
                  {settlement.status !== "Created" && (
                    <p className="text-sm text-muted-foreground">This settlement is already {settlement.status}.</p>
                  )}
                </div>
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
