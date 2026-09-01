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
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { createMerchantSettlementSchema, type CreateMerchantSettlementFormValues } from "@/features/cod/schema";
import { useCreateMerchantSettlement } from "@/features/cod/hooks/useCreateMerchantSettlement";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { codErrorMessage } from "@/features/cod/lib/codErrorMessage";

export function MerchantSettlementCreateSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const openDetail = useCodPanelStore((s) => s.openMerchantSettlementDetail);
  const isOpen = panel.mode === "merchant-settlement-create";

  const createSettlement = useCreateMerchantSettlement();
  const { data: merchants } = useMerchants();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    handleSubmit,
    control,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateMerchantSettlementFormValues>({ resolver: zodResolver(createMerchantSettlementSchema) });

  async function onSubmit(values: CreateMerchantSettlementFormValues) {
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
          <SheetTitle>New Merchant Settlement</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="merchantId">Merchant</Label>
            <Controller
              name="merchantId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="merchantId" aria-label="Merchant">
                    <SelectValue placeholder="Select merchant" />
                  </SelectTrigger>
                  <SelectContent>
                    {merchants?.map((m) => (
                      <SelectItem key={m.id} value={m.id}>{m.displayName}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {errors.merchantId && <p className="text-sm text-destructive">{errors.merchantId.message}</p>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="periodFrom">Period From</Label>
            <Controller
              name="periodFrom"
              control={control}
              render={({ field }) => <Input id="periodFrom" aria-label="Period From" type="date" {...field} />}
            />
            {errors.periodFrom && <p className="text-sm text-destructive">{errors.periodFrom.message}</p>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="periodTo">Period To</Label>
            <Controller
              name="periodTo"
              control={control}
              render={({ field }) => <Input id="periodTo" aria-label="Period To" type="date" {...field} />}
            />
            {errors.periodTo && <p className="text-sm text-destructive">{errors.periodTo.message}</p>}
          </div>
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Settlement"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
