"use client";

import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { FieldError } from "@/components/ui/field-error";
import { ErrorState } from "@/components/states/ErrorState";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useMerchantSettlement } from "@/features/cod/hooks/useMerchantSettlement";
import { useAdjustments } from "@/features/cod/hooks/useAdjustments";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { buildSettlementBreakdown } from "@/features/cod/lib/settlementBreakdown";
import { formatDate, formatRs } from "@/features/cod/lib/codFormat";
import { useSettleMerchantSettlement } from "@/features/cod/hooks/useSettleMerchantSettlement";
import { useRequestAdjustment } from "@/features/cod/hooks/useRequestAdjustment";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { codErrorMessage } from "@/features/cod/lib/codErrorMessage";
import { requestAdjustmentSchema, type RequestAdjustmentFormValues } from "@/features/cod/schema";
import type { MerchantSettlementItem } from "@/features/cod/types";


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
          {isSubmitting ? <Spinner className="h-4 w-4" /> : "Request adjustment"}
        </Button>
        <Button type="button" size="sm" variant="outline" onClick={onDone}>Cancel</Button>
      </div>
    </form>
  );
}


function BreakdownRow({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <li className={`flex justify-between py-2 ${strong ? "pt-3 font-semibold" : "border-b border-border"}`}>
      <span>{label}</span>
      <span className={`tabular-nums ${strong ? "text-base text-primary" : "font-medium"}`}>{value}</span>
    </li>
  );
}

export function MerchantSettlementDetailSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const isOpen = panel.mode === "merchant-settlement-detail";
  const id = panel.mode === "merchant-settlement-detail" ? panel.id : "";

  const [adjustingItemId, setAdjustingItemId] = useState<string | null>(null);
  const [tracedItemId, setTracedItemId] = useState<string | null>(null);
  const [settleError, setSettleError] = useState<string | null>(null);
  const canAdjust = usePermission("cod.adjust");
  const canSettle = usePermission("cod.settle");

  const { data: settlement, isLoading, isError, refetch } = useMerchantSettlement(id);
  // Server-narrowed to this settlement's item adjustments, so the caption never under-reports.
  const { data: adjustments, isError: adjustmentsError } = useAdjustments(
    { merchantSettlementId: id, applied: true, pageSize: 200 },
    { enabled: isOpen && Boolean(id) },
  );
  const { data: merchants } = useMerchants();
  const settleMutation = useSettleMerchantSettlement(id);

  const breakdown = settlement ? buildSettlementBreakdown(settlement, adjustments?.items ?? []) : null;
  const traced = settlement?.items.find((i) => i.id === tracedItemId) ?? settlement?.items[0];
  const merchantName = merchants?.find((m) => m.id === settlement?.merchantId)?.displayName ?? "Merchant";

  async function handleSettle() {
    setSettleError(null);
    try {
      await settleMutation.mutateAsync();
    } catch (error) {
      setSettleError(codErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) { setAdjustingItemId(null); setTracedItemId(null); close(); } }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-2xl">
        <SheetHeader className="sr-only">
          <SheetTitle>Merchant settlement</SheetTitle>
          <SheetDescription>Payable breakdown and line items</SheetDescription>
        </SheetHeader>
        <div className="space-y-6">
          {isLoading && <DetailSkeleton fieldRows={2} />}
          {isError && <ErrorState title="Couldn't load this settlement" description="Something went wrong." onRetry={() => refetch()} />}
          {settlement && breakdown && (
            <>
              <SheetHeaderBlock
                eyebrow="Merchant settlement"
                title={merchantName}
                status={<StatusBadge status={settlement.status} namespace="MerchantSettlement" />}
                facts={[
                  { label: "Settlement", value: <span className="font-mono text-xs">{settlement.id.slice(0, 8)}</span> },
                  { label: "Period", value: `${formatDate(settlement.periodFrom)} – ${formatDate(settlement.periodTo)}` },
                  { label: "Settled", value: settlement.settledAt ? formatDate(settlement.settledAt) : "—" },
                ]}
              />

              <div className="grid gap-5 md:grid-cols-[minmax(0,1fr)_240px]">
                <ul aria-label="Payable breakdown" className="text-sm">
                  <BreakdownRow
                    label={`${breakdown.parcelCount} delivered COD ${breakdown.parcelCount === 1 ? "parcel" : "parcels"}`}
                    value={formatRs(breakdown.codTotal)}
                  />
                  <BreakdownRow label="Delivery charges (booking-time prices)" value={`− ${formatRs(breakdown.courierFees)}`} />
                  <BreakdownRow label="Other deductions" value={`− ${formatRs(breakdown.otherDeductions)}`} />
                  {adjustmentsError ? (
                    <li className="border-b border-border pb-2 text-xs text-muted-foreground">Adjustment total unavailable</li>
                  ) : adjustments && breakdown.adjustmentsIncluded > 0 ? (
                    <li className="border-b border-border pb-2 text-xs text-muted-foreground">
                      Includes {formatRs(breakdown.adjustmentsIncluded)} from approved adjustments
                    </li>
                  ) : adjustments && breakdown.adjustmentsIncluded < 0 ? (
                    <li className="border-b border-border pb-2 text-xs text-muted-foreground">
                      Reduces deductions by {formatRs(-breakdown.adjustmentsIncluded)}
                    </li>
                  ) : null}
                  <BreakdownRow label="Merchant payable" value={formatRs(breakdown.payable)} strong />
                  {breakdown.mismatch && (
                    <li role="alert" className="mt-2 rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-xs text-destructive">
                      Lines don&apos;t add up to the payable — contact finance. Lines total {formatRs(breakdown.linesNet)}; payable is {formatRs(breakdown.payable)}.
                    </li>
                  )}
                </ul>

                {traced && (
                  <section aria-label="Trace one line" className="flex flex-col gap-2 rounded-lg bg-accent p-4 text-sm">
                    <span className="font-mono text-xs tracking-widest text-muted-foreground">TRACE ONE LINE</span>
                    <span className="font-mono font-medium">{traced.shipmentAwb}</span>
                    <span className="flex justify-between"><span>COD collected</span><span className="tabular-nums">{formatRs(traced.codAmount)}</span></span>
                    <span className="flex justify-between"><span>Courier fee</span><span className="tabular-nums">− {formatRs(traced.courierFee)}</span></span>
                    <span className="flex justify-between"><span>Other deductions</span><span className="tabular-nums">− {formatRs(traced.otherDeductions)}</span></span>
                    <span className="flex justify-between border-t border-border pt-2 font-semibold"><span>Payable</span><span className="tabular-nums">{formatRs(traced.netPayable)}</span></span>
                  </section>
                )}
              </div>

              <div className="space-y-2">
                <h3 className="text-sm font-medium">Line items</h3>
                {settlement.items.length === 0 ? (
                  <p className="rounded-lg border border-border p-3 text-sm text-muted-foreground">No parcels in this settlement.</p>
                ) : (
                  <ul className="divide-y divide-border rounded-lg border border-border">
                    {settlement.items.map((item) => (
                      <li key={item.id} className="p-3 text-sm">
                        <div className="flex flex-wrap items-center gap-3">
                          <button
                            type="button"
                            aria-label={`Trace ${item.shipmentAwb}`}
                            onClick={() => setTracedItemId(item.id)}
                            className="font-mono font-medium text-foreground hover:underline"
                          >
                            {item.shipmentAwb}
                          </button>
                          <span className="ml-auto grid grid-cols-4 gap-3 text-right tabular-nums text-muted-foreground">
                            <span>{formatRs(item.codAmount)}</span>
                            <span>− {formatRs(item.courierFee)}</span>
                            <span>− {formatRs(item.otherDeductions)}</span>
                            <span className="font-medium text-foreground">{formatRs(item.netPayable)}</span>
                          </span>
                          {canAdjust && settlement.status === "Created" && adjustingItemId !== item.id && (
                            <Button type="button" size="sm" variant="outline" onClick={() => setAdjustingItemId(item.id)}>
                              Request adjustment
                            </Button>
                          )}
                        </div>
                        {adjustingItemId === item.id && <AdjustmentForm item={item} onDone={() => setAdjustingItemId(null)} />}
                      </li>
                    ))}
                  </ul>
                )}
              </div>

              {canSettle && (
                <div className="space-y-2">
                  {settleError && <FieldError id="settle-error">{settleError}</FieldError>}
                  <Button type="button" disabled={settlement.status !== "Created" || settleMutation.isPending} onClick={handleSettle}>
                    {settleMutation.isPending ? <Spinner className="h-4 w-4" /> : "Mark settled"}
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
