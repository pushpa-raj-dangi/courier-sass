"use client";

import { useState } from "react";
import { useForm, Controller, FormProvider } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { FieldError } from "@/components/ui/field-error";
import { GeographyPicker, type GeographyValue } from "@/features/branches/GeographyPicker";
import { toSafeQuoteAmount } from "@/lib/pricing/toSafeQuoteAmount";
import { useMerchantBranches } from "@/features/merchant-shipments/hooks/useMerchantBranches";
import { useMerchantServiceTypes } from "@/features/merchant-shipments/hooks/useMerchantServiceTypes";
import { useMerchantShipmentQuote } from "@/features/merchant-shipments/hooks/useMerchantShipmentQuote";
import { useCreateMerchantShipment } from "@/features/merchant-shipments/hooks/useCreateMerchantShipment";
import { useMerchantShipmentPanelStore } from "@/features/merchant-shipments/store/merchantShipmentPanelStore";
import { createMerchantShipmentSchema, type CreateMerchantShipmentFormValues } from "@/features/merchant-shipments/schema";
import { MerchantItemsField } from "@/features/merchant-shipments/components/MerchantItemsField";
import { MerchantSenderSection } from "@/features/merchant-shipments/components/MerchantSenderSection";
import { ServiceTypeCards } from "@/features/merchant-shipments/components/ServiceTypeCards";
import { BookingPriceFooter } from "@/features/merchant-shipments/components/BookingPriceFooter";
import { bookingFigures } from "@/features/merchant-shipments/lib/bookingFigures";

function SectionEyebrow({ children }: { children: string }) {
  return <h3 className="font-mono text-2xs font-medium uppercase tracking-wider text-muted-foreground">{children}</h3>;
}

export function MerchantShipmentCreateSheet() {
  const panel = useMerchantShipmentPanelStore((s) => s.panel);
  const close = useMerchantShipmentPanelStore((s) => s.close);
  const openDetail = useMerchantShipmentPanelStore((s) => s.openDetail);
  const isOpen = panel.mode === "create";

  const createShipment = useCreateMerchantShipment();
  const { data: branches } = useMerchantBranches();
  const { data: serviceTypes } = useMerchantServiceTypes();
  const [serverError, setServerError] = useState<string | null>(null);

  const methods = useForm<CreateMerchantShipmentFormValues>({
    resolver: zodResolver(createMerchantShipmentSchema),
    // Sender text fields start as "" (as when they were always-registered inputs) so a collapsed
    // sender section still yields the schema's own "required" messages, not a type error.
    defaultValues: {
      items: [{ description: "", quantity: 1, unitValue: 0 }],
      wantsInsurance: false,
      senderName: "",
      senderPhone: "",
      senderAddressLine: "",
    },
  });
  const { register, handleSubmit, control, watch, setValue, reset, formState: { errors, isSubmitting } } = methods;

  const recipientGeography: GeographyValue = {
    provinceId: watch("recipientProvinceId") ?? null,
    districtId: watch("recipientDistrictId") ?? null,
    localLevelId: watch("recipientLocalLevelId") ?? null,
    wardNumber: watch("recipientWardNumber") ?? null,
  };

  const [originBranchId, destinationBranchId, weightKg, serviceTypeId, codAmount, wantsInsurance, items] =
    watch(["originBranchId", "destinationBranchId", "weightKg", "serviceTypeId", "codAmount", "wantsInsurance", "items"]);

  const declaredValue = (items ?? []).reduce((sum, item) => sum + (item.quantity || 0) * (item.unitValue || 0), 0);

  const quoteInput =
    originBranchId && destinationBranchId && weightKg && serviceTypeId
      ? {
          originBranchId,
          destinationBranchId,
          weightKg,
          serviceTypeId,
          codAmount: toSafeQuoteAmount(codAmount),
          declaredValue,
          wantsInsurance: wantsInsurance ?? false,
        }
      : null;
  const { data: quote, isFetching: isQuoting, isError: quoteFailed, refetch: refetchQuote } = useMerchantShipmentQuote(quoteInput);
  // Read-only, server-computed figures — never editable, per CLAUDE.md §33.
  const figures = quote ? bookingFigures(quote, toSafeQuoteAmount(codAmount)) : null;

  async function onSubmit(values: CreateMerchantShipmentFormValues) {
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
      <SheetContent className="flex w-full flex-col gap-0 p-0 sm:max-w-[600px]">
        <SheetHeader className="space-y-1 border-b border-border px-6 py-4">
          <p className="font-mono text-2xs font-medium uppercase tracking-wider text-muted-foreground">NEW SHIPMENT</p>
          <SheetTitle>Book a shipment</SheetTitle>
          <SheetDescription>The price is locked the moment you book.</SheetDescription>
        </SheetHeader>
        <FormProvider {...methods}>
          <form onSubmit={handleSubmit(onSubmit)} className="flex min-h-0 flex-1 flex-col" noValidate>
            <div className="flex-1 space-y-8 overflow-y-auto px-6 py-6">
              <section className="space-y-4">
                <SectionEyebrow>01 · RECIPIENT</SectionEyebrow>
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
                  source="merchant"
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
                <SectionEyebrow>02 · PARCEL</SectionEyebrow>
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
                <MerchantItemsField />
              </section>

              <section className="space-y-4">
                <SectionEyebrow>03 · SERVICE AND PAYMENT</SectionEyebrow>
                <div className="space-y-2">
                  <Label id="serviceType-label">Service type</Label>
                  <Controller
                    name="serviceTypeId"
                    control={control}
                    render={({ field }) => (
                      <ServiceTypeCards
                        serviceTypes={serviceTypes}
                        value={field.value}
                        onChange={field.onChange}
                        invalid={!!errors.serviceTypeId}
                        describedBy={errors.serviceTypeId ? "serviceTypeId-error" : undefined}
                      />
                    )}
                  />
                  {errors.serviceTypeId && <FieldError id="serviceTypeId-error">{errors.serviceTypeId.message}</FieldError>}
                </div>
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <div className="space-y-2">
                    <Label htmlFor="originBranchId">Pickup from</Label>
                    <Controller
                      name="originBranchId"
                      control={control}
                      render={({ field }) => (
                        <Select value={field.value} onValueChange={field.onChange}>
                          <SelectTrigger
                            id="originBranchId"
                            aria-label="Pickup from"
                            aria-invalid={!!errors.originBranchId}
                            aria-describedby={errors.originBranchId ? "originBranchId-error" : undefined}
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
                    {errors.originBranchId && <FieldError id="originBranchId-error">{errors.originBranchId.message}</FieldError>}
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="destinationBranchId">Deliver from</Label>
                    <Controller
                      name="destinationBranchId"
                      control={control}
                      render={({ field }) => (
                        <Select value={field.value} onValueChange={field.onChange}>
                          <SelectTrigger
                            id="destinationBranchId"
                            aria-label="Deliver from"
                            aria-invalid={!!errors.destinationBranchId}
                            aria-describedby={errors.destinationBranchId ? "destinationBranchId-error" : undefined}
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
                    {errors.destinationBranchId && <FieldError id="destinationBranchId-error">{errors.destinationBranchId.message}</FieldError>}
                  </div>
                </div>
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <div className="space-y-2">
                    <Label htmlFor="codAmount">COD amount</Label>
                    <Input
                      id="codAmount"
                      type="number"
                      aria-invalid={!!errors.codAmount}
                      aria-describedby={errors.codAmount ? "codAmount-error" : undefined}
                      {...register("codAmount", {
                        // A blank input reaches setValueAs as "" — valueAsNumber alone would turn
                        // that into NaN, which Zod's z.number() rejects as invalid, forcing every
                        // merchant booking a plain non-COD shipment to explicitly type "0" (found
                        // via Frontend Phase F10's final whole-branch review). Coercing an empty
                        // string to 0 here matches the live-quote-preview's toSafeQuoteAmount
                        // coercion for the same field, without touching the schema's inferred type.
                        setValueAs: (value) => (value === "" ? 0 : Number(value)),
                      })}
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
                    <Label htmlFor="wantsInsurance">Insure this parcel</Label>
                  </div>
                </div>
                <MerchantSenderSection />
              </section>

              {serverError && <FieldError id="merchant-shipment-server-error">{serverError}</FieldError>}
            </div>
            <BookingPriceFooter
              figures={figures}
              isQuoting={isQuoting}
              quoteFailed={quoteFailed}
              onRetryQuote={() => refetchQuote()}
              isSubmitting={isSubmitting}
              onCancel={close}
            />
          </form>
        </FormProvider>
      </SheetContent>
    </Sheet>
  );
}
