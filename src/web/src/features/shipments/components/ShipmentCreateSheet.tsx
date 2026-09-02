"use client";

import { useState } from "react";
import { useForm, Controller, FormProvider } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { GeographyPicker, type GeographyValue } from "@/features/branches/GeographyPicker";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { createShipmentSchema, type CreateShipmentFormValues } from "@/features/shipments/schema";
import { useCreateShipment } from "@/features/shipments/hooks/useCreateShipment";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { useServiceTypes } from "@/features/shipments/hooks/useServiceTypes";
import { useShipmentQuote } from "@/features/shipments/hooks/useShipmentQuote";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import { ItemsField } from "@/features/shipments/components/ItemsField";

export function ShipmentCreateSheet() {
  const panel = useShipmentPanelStore((s) => s.panel);
  const close = useShipmentPanelStore((s) => s.close);
  const openDetail = useShipmentPanelStore((s) => s.openDetail);
  const isOpen = panel.mode === "create";

  const createShipment = useCreateShipment();
  const { data: merchants } = useMerchants();
  const { data: serviceTypes } = useServiceTypes();
  const { data: branches } = useBranches();
  const [serverError, setServerError] = useState<string | null>(null);

  const methods = useForm<CreateShipmentFormValues>({
    resolver: zodResolver(createShipmentSchema),
    defaultValues: { items: [{ description: "", quantity: 1, unitValue: 0 }], wantsInsurance: false },
  });
  const { register, handleSubmit, control, watch, setValue, reset, formState: { errors, isSubmitting } } = methods;

  const recipientGeography: GeographyValue = {
    provinceId: watch("recipientProvinceId") ?? null,
    districtId: watch("recipientDistrictId") ?? null,
    localLevelId: watch("recipientLocalLevelId") ?? null,
    wardNumber: watch("recipientWardNumber") ?? null,
  };
  const senderGeography: GeographyValue = {
    provinceId: watch("senderProvinceId") ?? null,
    districtId: watch("senderDistrictId") ?? null,
    localLevelId: watch("senderLocalLevelId") ?? null,
    wardNumber: watch("senderWardNumber") ?? null,
  };

  const [originBranchId, destinationBranchId, weightKg, serviceTypeId, codAmount, wantsInsurance, merchantId, items] =
    watch(["originBranchId", "destinationBranchId", "weightKg", "serviceTypeId", "codAmount", "wantsInsurance", "merchantId", "items"]);

  const declaredValue = (items ?? []).reduce((sum, item) => sum + (item.quantity || 0) * (item.unitValue || 0), 0);

  const quoteInput =
    originBranchId && destinationBranchId && weightKg && serviceTypeId
      ? {
          originBranchId,
          destinationBranchId,
          weightKg,
          serviceTypeId,
          codAmount: codAmount ?? 0,
          declaredValue,
          wantsInsurance: wantsInsurance ?? false,
          merchantId: merchantId ?? null,
        }
      : null;
  const { data: quote, isFetching: isQuoting } = useShipmentQuote(quoteInput);

  const currencyFormatter = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" });

  async function onSubmit(values: CreateShipmentFormValues) {
    setServerError(null);
    try {
      const created = await createShipment.mutateAsync(values);
      reset();
      openDetail(created.awb);
    } catch {
      setServerError("Couldn't create the shipment. Check the fields and try again.");
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-2xl">
        <SheetHeader>
          <SheetTitle>New Shipment</SheetTitle>
        </SheetHeader>
        <FormProvider {...methods}>
          <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-6" noValidate>
            <section className="space-y-4">
              <h3 className="text-sm font-semibold text-foreground">Route</h3>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="merchantId">Merchant</Label>
                  <Controller
                    name="merchantId"
                    control={control}
                    render={({ field }) => (
                      <Select value={field.value} onValueChange={field.onChange}>
                        <SelectTrigger
                          id="merchantId"
                          aria-label="Merchant"
                          aria-invalid={!!errors.merchantId}
                          aria-describedby={errors.merchantId ? "merchantId-error" : undefined}
                        >
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
                  {errors.merchantId && <FieldError id="merchantId-error">{errors.merchantId.message}</FieldError>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="serviceTypeId">Service Type</Label>
                  <Controller
                    name="serviceTypeId"
                    control={control}
                    render={({ field }) => (
                      <Select value={field.value} onValueChange={field.onChange}>
                        <SelectTrigger
                          id="serviceTypeId"
                          aria-label="Service type"
                          aria-invalid={!!errors.serviceTypeId}
                          aria-describedby={errors.serviceTypeId ? "serviceTypeId-error" : undefined}
                        >
                          <SelectValue placeholder="Select service type" />
                        </SelectTrigger>
                        <SelectContent>
                          {serviceTypes?.map((s) => (
                            <SelectItem key={s.id} value={s.id}>{s.name}</SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    )}
                  />
                  {errors.serviceTypeId && <FieldError id="serviceTypeId-error">{errors.serviceTypeId.message}</FieldError>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="originBranchId">Origin Branch</Label>
                  <Controller
                    name="originBranchId"
                    control={control}
                    render={({ field }) => (
                      <Select value={field.value} onValueChange={field.onChange}>
                        <SelectTrigger
                          id="originBranchId"
                          aria-label="Origin branch"
                          aria-invalid={!!errors.originBranchId}
                          aria-describedby={errors.originBranchId ? "originBranchId-error" : undefined}
                        >
                          <SelectValue placeholder="Select origin branch" />
                        </SelectTrigger>
                        <SelectContent>
                          {branches?.map((b) => (
                            <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    )}
                  />
                  {errors.originBranchId && <FieldError id="originBranchId-error">{errors.originBranchId.message}</FieldError>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="destinationBranchId">Destination Branch</Label>
                  <Controller
                    name="destinationBranchId"
                    control={control}
                    render={({ field }) => (
                      <Select value={field.value} onValueChange={field.onChange}>
                        <SelectTrigger
                          id="destinationBranchId"
                          aria-label="Destination branch"
                          aria-invalid={!!errors.destinationBranchId}
                          aria-describedby={errors.destinationBranchId ? "destinationBranchId-error" : undefined}
                        >
                          <SelectValue placeholder="Select destination branch" />
                        </SelectTrigger>
                        <SelectContent>
                          {branches?.map((b) => (
                            <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    )}
                  />
                  {errors.destinationBranchId && <FieldError id="destinationBranchId-error">{errors.destinationBranchId.message}</FieldError>}
                </div>
              </div>
            </section>

            <section className="space-y-4">
              <h3 className="text-sm font-semibold text-foreground">Package</h3>
              <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
                <div className="space-y-2">
                  <Label htmlFor="weightKg">Weight (kg)</Label>
                  <Input
                    id="weightKg"
                    type="number"
                    step="0.1"
                    aria-invalid={!!errors.weightKg}
                    aria-describedby={errors.weightKg ? "weightKg-error" : undefined}
                    {...register("weightKg", { valueAsNumber: true })}
                  />
                  {errors.weightKg && <FieldError id="weightKg-error">{errors.weightKg.message}</FieldError>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="lengthCm">Length (cm)</Label>
                  <Input
                    id="lengthCm"
                    type="number"
                    {...register("lengthCm", { setValueAs: (v) => (v === "" ? undefined : Number(v)) })}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="widthCm">Width (cm)</Label>
                  <Input
                    id="widthCm"
                    type="number"
                    {...register("widthCm", { setValueAs: (v) => (v === "" ? undefined : Number(v)) })}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="heightCm">Height (cm)</Label>
                  <Input
                    id="heightCm"
                    type="number"
                    {...register("heightCm", { setValueAs: (v) => (v === "" ? undefined : Number(v)) })}
                  />
                </div>
              </div>
              <ItemsField />
            </section>

            <section className="space-y-4">
              <h3 className="text-sm font-semibold text-foreground">Money</h3>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="codAmount">COD Amount</Label>
                  <Input
                    id="codAmount"
                    type="number"
                    aria-invalid={!!errors.codAmount}
                    aria-describedby={errors.codAmount ? "codAmount-error" : undefined}
                    {...register("codAmount", { valueAsNumber: true })}
                  />
                  {errors.codAmount && <FieldError id="codAmount-error">{errors.codAmount.message}</FieldError>}
                </div>
                <div className="flex items-center gap-2 pt-6">
                  <Controller
                    name="wantsInsurance"
                    control={control}
                    render={({ field }) => (
                      <Checkbox id="wantsInsurance" checked={field.value} onCheckedChange={field.onChange} />
                    )}
                  />
                  <Label htmlFor="wantsInsurance">Wants Insurance</Label>
                </div>
              </div>

              {/* Read-only, server-computed estimate — never editable, per CLAUDE.md §33 */}
              <div className="rounded-lg border border-border bg-muted/50 p-4">
                <p className="mb-2 text-xs text-muted-foreground">Estimated Price (recalculates as you fill in the route and package)</p>
                {isQuoting && <Spinner className="h-4 w-4" />}
                {quote && !isQuoting && (
                  <p className="font-sans text-lg font-semibold text-foreground">
                    {currencyFormatter.format(quote.total)}
                  </p>
                )}
                {!quote && !isQuoting && <p className="text-sm text-muted-foreground">—</p>}
              </div>
            </section>

            <section className="space-y-4">
              <h3 className="text-sm font-semibold text-foreground">Recipient</h3>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="recipientName">Name</Label>
                  <Input
                    id="recipientName"
                    aria-invalid={!!errors.recipientName}
                    aria-describedby={errors.recipientName ? "recipientName-error" : undefined}
                    {...register("recipientName")}
                  />
                  {errors.recipientName && <FieldError id="recipientName-error">{errors.recipientName.message}</FieldError>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="recipientPhone">Phone</Label>
                  <Input
                    id="recipientPhone"
                    aria-invalid={!!errors.recipientPhone}
                    aria-describedby={errors.recipientPhone ? "recipientPhone-error" : undefined}
                    {...register("recipientPhone")}
                  />
                  {errors.recipientPhone && <FieldError id="recipientPhone-error">{errors.recipientPhone.message}</FieldError>}
                </div>
              </div>
              <GeographyPicker
                idPrefix="recipient"
                value={recipientGeography}
                onChange={(next) => {
                  setValue("recipientProvinceId", next.provinceId ?? "");
                  setValue("recipientDistrictId", next.districtId ?? "");
                  setValue("recipientLocalLevelId", next.localLevelId ?? "");
                  setValue("recipientWardNumber", next.wardNumber ?? undefined as never);
                }}
              />
              <div className="space-y-2">
                <Label htmlFor="recipientAddressLine">Address</Label>
                <Input
                  id="recipientAddressLine"
                  aria-invalid={!!errors.recipientAddressLine}
                  aria-describedby={errors.recipientAddressLine ? "recipientAddressLine-error" : undefined}
                  {...register("recipientAddressLine")}
                />
                {errors.recipientAddressLine && <FieldError id="recipientAddressLine-error">{errors.recipientAddressLine.message}</FieldError>}
              </div>
            </section>

            <section className="space-y-4">
              <h3 className="text-sm font-semibold text-foreground">Sender</h3>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="senderName">Name</Label>
                  <Input
                    id="senderName"
                    aria-invalid={!!errors.senderName}
                    aria-describedby={errors.senderName ? "senderName-error" : undefined}
                    {...register("senderName")}
                  />
                  {errors.senderName && <FieldError id="senderName-error">{errors.senderName.message}</FieldError>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="senderPhone">Phone</Label>
                  <Input
                    id="senderPhone"
                    aria-invalid={!!errors.senderPhone}
                    aria-describedby={errors.senderPhone ? "senderPhone-error" : undefined}
                    {...register("senderPhone")}
                  />
                  {errors.senderPhone && <FieldError id="senderPhone-error">{errors.senderPhone.message}</FieldError>}
                </div>
              </div>
              <GeographyPicker
                idPrefix="sender"
                value={senderGeography}
                onChange={(next) => {
                  setValue("senderProvinceId", next.provinceId ?? "");
                  setValue("senderDistrictId", next.districtId ?? "");
                  setValue("senderLocalLevelId", next.localLevelId ?? "");
                  setValue("senderWardNumber", next.wardNumber ?? undefined as never);
                }}
              />
              <div className="space-y-2">
                <Label htmlFor="senderAddressLine">Address</Label>
                <Input
                  id="senderAddressLine"
                  aria-invalid={!!errors.senderAddressLine}
                  aria-describedby={errors.senderAddressLine ? "senderAddressLine-error" : undefined}
                  {...register("senderAddressLine")}
                />
                {errors.senderAddressLine && <FieldError id="senderAddressLine-error">{errors.senderAddressLine.message}</FieldError>}
              </div>
            </section>

            {serverError && <FieldError id="shipment-server-error">{serverError}</FieldError>}
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Shipment"}
            </Button>
          </form>
        </FormProvider>
      </SheetContent>
    </Sheet>
  );
}
