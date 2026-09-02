"use client";

import { useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { createRiderSettlementSchema, type CreateRiderSettlementFormValues } from "@/features/cod/schema";
import { useCreateRiderSettlement } from "@/features/cod/hooks/useCreateRiderSettlement";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { codErrorMessage } from "@/features/cod/lib/codErrorMessage";

export function RiderSettlementCreateSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const openDetail = useCodPanelStore((s) => s.openRiderSettlementDetail);
  const isOpen = panel.mode === "rider-settlement-create";

  const createSettlement = useCreateRiderSettlement();
  const { data: branches } = useBranches();
  const { data: riders } = useRiders();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    handleSubmit,
    control,
    watch,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateRiderSettlementFormValues>({ resolver: zodResolver(createRiderSettlementSchema) });

  const selectedBranchId = watch("branchId");
  const eligibleRiders = riders?.filter((r) => r.isActive && r.baseBranchId === selectedBranchId);

  async function onSubmit(values: CreateRiderSettlementFormValues) {
    setServerError(null);
    try {
      const created = await createSettlement.mutateAsync(values);
      reset();
      openDetail(created.id);
    } catch (error) {
      setServerError(codErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>New Rider Settlement</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="branchId">Branch</Label>
            <Controller
              name="branchId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger
                    id="branchId"
                    aria-label="Branch"
                    aria-invalid={!!errors.branchId}
                    aria-describedby={errors.branchId ? "branchId-error" : undefined}
                  >
                    <SelectValue placeholder="Select branch" />
                  </SelectTrigger>
                  <SelectContent>
                    {branches?.map((b) => (
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
                  <SelectTrigger
                    id="riderId"
                    aria-label="Rider"
                    aria-invalid={!!errors.riderId}
                    aria-describedby={errors.riderId ? "riderId-error" : undefined}
                  >
                    <SelectValue placeholder="Select rider" />
                  </SelectTrigger>
                  <SelectContent>
                    {eligibleRiders?.map((r) => (
                      <SelectItem key={r.id} value={r.id}>{r.licenseNumber}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {errors.riderId && <FieldError id="riderId-error">{errors.riderId.message}</FieldError>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="declaredAmount">Declared Amount</Label>
            <Controller
              name="declaredAmount"
              control={control}
              render={({ field }) => (
                <Input
                  id="declaredAmount"
                  aria-label="Declared Amount"
                  type="number"
                  step="0.01"
                  aria-invalid={!!errors.declaredAmount}
                  aria-describedby={errors.declaredAmount ? "declaredAmount-error" : undefined}
                  value={field.value ?? ""}
                  onChange={(e) => field.onChange(e.target.value === "" ? undefined : Number(e.target.value))}
                />
              )}
            />
            {errors.declaredAmount && <FieldError id="declaredAmount-error">{errors.declaredAmount.message}</FieldError>}
          </div>
          {serverError && <FieldError id="rider-settlement-server-error">{serverError}</FieldError>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Settlement"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
