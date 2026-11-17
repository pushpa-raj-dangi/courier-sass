"use client";

import { useState } from "react";
import { useFormContext } from "react-hook-form";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { FieldError } from "@/components/ui/field-error";
import { GeographyPicker, type GeographyValue } from "@/features/branches/GeographyPicker";
import type { CreateMerchantShipmentFormValues } from "@/features/merchant-shipments/schema";

/** Sender summary line; "change" reveals the full sender fields. Stays open while any sender field has an error. */
export function MerchantSenderSection() {
  const { register, watch, setValue, formState: { errors } } = useFormContext<CreateMerchantShipmentFormValues>();
  const [expanded, setExpanded] = useState(false);

  const hasError = !!(
    errors.senderName || errors.senderPhone || errors.senderProvinceId || errors.senderDistrictId ||
    errors.senderLocalLevelId || errors.senderWardNumber || errors.senderAddressLine
  );
  const [senderName, senderPhone] = watch(["senderName", "senderPhone"]);
  const geography: GeographyValue = {
    provinceId: watch("senderProvinceId") ?? null,
    districtId: watch("senderDistrictId") ?? null,
    localLevelId: watch("senderLocalLevelId") ?? null,
    wardNumber: watch("senderWardNumber") ?? null,
  };

  if (!expanded && !hasError) {
    return (
      <p className="flex flex-wrap items-center gap-1.5 text-sm text-muted-foreground">
        <span>Sender: {senderName || "not set"}{senderPhone ? ` · ${senderPhone}` : ""} ·</span>
        <button type="button" className="font-medium text-foreground underline" onClick={() => setExpanded(true)}>
          change
        </button>
      </p>
    );
  }

  return (
    <div className="space-y-4 rounded-lg border border-border p-4">
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div className="space-y-2">
          <Label htmlFor="senderName">Sender name</Label>
          <Input
            id="senderName"
            aria-invalid={!!errors.senderName}
            aria-describedby={errors.senderName ? "senderName-error" : undefined}
            {...register("senderName")}
          />
          {errors.senderName && <FieldError id="senderName-error">{errors.senderName.message}</FieldError>}
        </div>
        <div className="space-y-2">
          <Label htmlFor="senderPhone">Sender phone</Label>
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
        source="merchant"
        value={geography}
        onChange={(next) => {
          setValue("senderProvinceId", next.provinceId ?? "");
          setValue("senderDistrictId", next.districtId ?? "");
          setValue("senderLocalLevelId", next.localLevelId ?? "");
          setValue("senderWardNumber", next.wardNumber ?? undefined as never);
        }}
      />
      <div className="space-y-2">
        <Label htmlFor="senderAddressLine">Sender address</Label>
        <Input
          id="senderAddressLine"
          aria-invalid={!!errors.senderAddressLine}
          aria-describedby={errors.senderAddressLine ? "senderAddressLine-error" : undefined}
          {...register("senderAddressLine")}
        />
        {errors.senderAddressLine && <FieldError id="senderAddressLine-error">{errors.senderAddressLine.message}</FieldError>}
      </div>
    </div>
  );
}
