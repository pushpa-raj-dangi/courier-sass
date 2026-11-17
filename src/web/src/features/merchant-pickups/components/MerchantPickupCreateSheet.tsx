"use client";

import { useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Textarea } from "@/components/ui/textarea";
import { DatePicker } from "@/components/ui/date-picker";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useMerchantShipments } from "@/features/merchant-shipments/hooks/useMerchantShipments";
import { EMPTY_MERCHANT_LIST_PARAMS } from "@/features/merchant-shipments/lib/listParams";
import { useCreateMerchantPickup } from "@/features/merchant-pickups/hooks/useCreateMerchantPickup";
import { useMerchantPickupPanelStore } from "@/features/merchant-pickups/store/merchantPickupPanelStore";
import { createMerchantPickupSchema, type CreateMerchantPickupFormValues } from "@/features/merchant-pickups/schema";

export function MerchantPickupCreateSheet() {
  const panel = useMerchantPickupPanelStore((s) => s.panel);
  const close = useMerchantPickupPanelStore((s) => s.close);
  const isOpen = panel.mode === "create";

  const createPickup = useCreateMerchantPickup();
  // First page is enough to populate the picker — a merchant with more than 20 freshly-booked,
  // not-yet-picked-up shipments waiting at once is a scale this phase doesn't need to solve for.
  const { data: shipments } = useMerchantShipments(EMPTY_MERCHANT_LIST_PARAMS, 20);
  const bookedShipments = (shipments?.items ?? []).filter((s) => s.currentStatus === "Created");
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    control,
    watch,
    setValue,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateMerchantPickupFormValues>({
    resolver: zodResolver(createMerchantPickupSchema),
    defaultValues: { shipmentIds: [] },
  });

  const selectedIds = watch("shipmentIds");

  function toggleShipment(id: string, checked: boolean) {
    if (checked) {
      setValue("shipmentIds", [...selectedIds, id]);
    } else {
      setValue("shipmentIds", selectedIds.filter((existing) => existing !== id));
    }
  }

  async function onSubmit(values: CreateMerchantPickupFormValues) {
    setServerError(null);
    try {
      await createPickup.mutateAsync(values);
      reset({ shipmentIds: [] });
      close();
    } catch {
      setServerError("Couldn't create the pickup request. Check the fields and try again.");
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Request a Pickup</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-6" noValidate>
          <div className="space-y-2">
            <Label>Shipments</Label>
            <div className="max-h-64 space-y-2 overflow-y-auto rounded-lg border border-border p-3">
              {bookedShipments.length === 0 && (
                <p className="text-sm text-muted-foreground">No freshly booked shipments waiting for pickup.</p>
              )}
              {bookedShipments.map((s) => (
                <div key={s.id} className="flex items-center gap-2">
                  <Checkbox
                    id={`pickup-shipment-${s.id}`}
                    checked={selectedIds.includes(s.id)}
                    onCheckedChange={(checked) => toggleShipment(s.id, checked === true)}
                  />
                  <Label htmlFor={`pickup-shipment-${s.id}`} className="font-normal">
                    {s.awb} — {s.recipientName}
                  </Label>
                </div>
              ))}
            </div>
            {errors.shipmentIds && <FieldError id="shipmentIds-error">{errors.shipmentIds.message}</FieldError>}
          </div>

          <div className="space-y-2">
            <Label htmlFor="requestedForDate">Requested Date</Label>
            <Controller
              name="requestedForDate"
              control={control}
              render={({ field }) => (
                <DatePicker
                  id="requestedForDate"
                  aria-label="Requested date"
                  value={field.value}
                  onChange={field.onChange}
                  aria-invalid={!!errors.requestedForDate}
                  aria-describedby={errors.requestedForDate ? "requestedForDate-error" : undefined}
                />
              )}
            />
            {errors.requestedForDate && <FieldError id="requestedForDate-error">{errors.requestedForDate.message}</FieldError>}
          </div>

          <div className="space-y-2">
            <Label htmlFor="notes">Notes</Label>
            <Textarea id="notes" {...register("notes")} />
          </div>

          {serverError && <FieldError id="merchant-pickup-server-error">{serverError}</FieldError>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Request Pickup"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
