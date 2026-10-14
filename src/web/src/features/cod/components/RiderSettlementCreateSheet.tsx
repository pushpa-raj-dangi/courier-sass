"use client";

import { useEffect, useMemo, useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Info } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { useSession } from "@/features/auth/hooks/useSession";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useRiderCash } from "@/features/cod/hooks/useRiderCash";
import { createRiderSettlementSchema, type CreateRiderSettlementFormValues } from "@/features/cod/schema";
import { useCreateRiderSettlement } from "@/features/cod/hooks/useCreateRiderSettlement";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { codErrorMessage } from "@/features/cod/lib/codErrorMessage";
import { describeVariance, formatRs, formatTime } from "@/features/cod/lib/codFormat";

export function RiderSettlementCreateSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const isOpen = panel.mode === "rider-settlement-create";
  const prefill = panel.mode === "rider-settlement-create" ? panel.prefill : undefined;

  const createSettlement = useCreateRiderSettlement();
  const { data: session } = useSession();
  const { data: branches } = useBranches();
  const ridersQuery = useRiders();
  const riders = ridersQuery.data;
  const names = usePeopleNames();
  const [serverError, setServerError] = useState<string | null>(null);
  // Same scope rule as the /cod page's branch picker; the server still enforces cod.reconcile scope.
  const scopedBranches = (branches ?? []).filter(
    (b) => session?.hasAllBranchesScope || session?.branches.some((s) => s.branchId === b.id),
  );

  const {
    handleSubmit,
    control,
    watch,
    reset,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<CreateRiderSettlementFormValues>({ resolver: zodResolver(createRiderSettlementSchema) });

  useEffect(() => {
    if (isOpen) {
      reset({ riderId: prefill?.riderId ?? "", branchId: prefill?.branchId ?? "", declaredAmount: undefined as unknown as number });
      setServerError(null);
    }
  }, [isOpen, prefill?.riderId, prefill?.branchId, reset]);

  const selectedBranchId = watch("branchId");
  const selectedRiderId = watch("riderId");
  const received = watch("declaredAmount");
  const cash = useRiderCash({ branchId: selectedBranchId || undefined }, { enabled: isOpen && Boolean(selectedBranchId) });
  const riderLabel = (id: string) => {
    const name = names.riderName(id);
    if (name !== "Rider") return name;
    return riders?.find((r) => r.id === id)?.licenseNumber ?? `Rider ${id.slice(0, 8)}`;
  };
  // Rider-cash rows are keyed by the collection's branch, not the rider's base branch, so a rider
  // holding cash here may be based elsewhere: offer both sets, de-duplicated.
  const riderOptions = useMemo(() => {
    if (!selectedBranchId) return [];
    const ids = new Set<string>();
    riders?.filter((r) => r.isActive && r.baseBranchId === selectedBranchId).forEach((r) => ids.add(r.id));
    cash.data?.items.filter((h) => h.branchId === selectedBranchId).forEach((h) => ids.add(h.riderId));
    return [...ids];
  }, [riders, cash.data, selectedBranchId]);
  const optionsSettled = Boolean(selectedBranchId) && !ridersQuery.isPending && !cash.isPending;

  // Never submit a rider the select can't show: once the options for the chosen branch have
  // loaded, drop a rider who isn't among them (e.g. after the branch was changed).
  useEffect(() => {
    if (!isOpen || !selectedRiderId || !optionsSettled) return;
    if (!riderOptions.includes(selectedRiderId)) setValue("riderId", "");
  }, [isOpen, selectedRiderId, optionsSettled, riderOptions, setValue]);

  const holding = cash.data?.items.find((h) => h.riderId === selectedRiderId && h.branchId === selectedBranchId);
  const preview = holding && typeof received === "number" && !Number.isNaN(received) ? describeVariance(received - holding.amount) : null;
  const selectedBranchName = selectedBranchId
    ? scopedBranches.find((b) => b.id === selectedBranchId)?.name ?? names.branchName(selectedBranchId)
    : null;
  const parcelWord = (n: number) => (n === 1 ? "COD parcel" : "COD parcels");

  async function onSubmit(values: CreateRiderSettlementFormValues) {
    setServerError(null);
    try {
      await createSettlement.mutateAsync(values);
      close();
    } catch (error) {
      setServerError(codErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Record deposit</SheetTitle>
          <SheetDescription>
            {holding
              ? `${riderLabel(holding.riderId)} at ${selectedBranchName} · ${holding.parcelCount} ${parcelWord(holding.parcelCount)} · last collected ${formatTime(holding.latestCollectedAt)}`
              : selectedBranchName
                ? `Count the cash a rider hands over at ${selectedBranchName} and record it against their collections.`
                : "Count the cash a rider hands over and record it against their collections."}
          </SheetDescription>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="branchId">Branch</Label>
            <Controller
              name="branchId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="branchId" aria-label="Branch" aria-invalid={!!errors.branchId} aria-describedby={errors.branchId ? "branchId-error" : undefined}>
                    <SelectValue placeholder="Select branch" />
                  </SelectTrigger>
                  <SelectContent>
                    {scopedBranches.map((b) => (
                      <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {errors.branchId && <FieldError id="branchId-error">{errors.branchId.message}</FieldError>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="riderId">Rider</Label>
            <Controller
              name="riderId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="riderId" aria-label="Rider" aria-invalid={!!errors.riderId} aria-describedby={errors.riderId ? "riderId-error" : undefined}>
                    <SelectValue placeholder="Select rider" />
                  </SelectTrigger>
                  <SelectContent>
                    {riderOptions.map((riderId) => (
                      <SelectItem key={riderId} value={riderId}>{riderLabel(riderId)}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {errors.riderId && <FieldError id="riderId-error">{errors.riderId.message}</FieldError>}
          </div>

          <dl className="space-y-2 rounded-lg border border-border p-4 text-sm">
            <div className="flex justify-between">
              <dt className="text-muted-foreground">Expected (held)</dt>
              <dd className="font-semibold tabular-nums">{holding ? formatRs(holding.amount) : "—"}</dd>
            </div>
            <div className="space-y-2">
              <Label htmlFor="declaredAmount">Cash received</Label>
              <Controller
                name="declaredAmount"
                control={control}
                render={({ field }) => (
                  <Input
                    id="declaredAmount"
                    aria-label="Cash received"
                    type="number"
                    step="0.01"
                    inputMode="decimal"
                    aria-invalid={!!errors.declaredAmount}
                    aria-describedby={errors.declaredAmount ? "declaredAmount-error" : undefined}
                    value={field.value ?? ""}
                    onChange={(e) => field.onChange(e.target.value === "" ? undefined : Number(e.target.value))}
                  />
                )}
              />
              {errors.declaredAmount && <FieldError id="declaredAmount-error">{errors.declaredAmount.message}</FieldError>}
            </div>
            <div className="flex justify-between border-t border-border pt-2">
              <dt className="font-medium">Variance <span className="text-xs font-normal text-muted-foreground">(preview)</span></dt>
              <dd className="font-semibold">{preview ? preview.label : "—"}</dd>
            </div>
          </dl>

          {preview && preview.tone !== "matched" && (
            <p className="flex gap-2 rounded-lg bg-muted p-3 text-xs text-muted-foreground">
              <Info className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              Shortage/overage reasons and approval need backend support. The deposit and its variance will be recorded.
            </p>
          )}

          <ComingSoonSlot className="p-4" title="Note denominations" description="Count cash by note (1000 × 30, 500 × 10…)." />
          <ComingSoonSlot className="p-4" title="Fonepay / QR split" description="Separate verified digital payments from cash." />

          {serverError && <FieldError id="rider-settlement-server-error">{serverError}</FieldError>}
          <Button type="submit" className="w-full" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Record deposit"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
