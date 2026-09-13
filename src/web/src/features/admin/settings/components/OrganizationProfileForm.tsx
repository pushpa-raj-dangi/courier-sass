"use client";

import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { Skeleton } from "@/components/ui/skeleton";
import { useOrganizationSettings } from "@/features/admin/settings/hooks/useOrganizationSettings";
import { useUpdateOrganizationProfile } from "@/features/admin/settings/hooks/useUpdateOrganizationProfile";
import { organizationProfileSchema, type OrganizationProfileFormValues } from "@/features/admin/settings/schema";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";

export function OrganizationProfileForm() {
  const { data, isLoading } = useOrganizationSettings();
  const updateProfile = useUpdateOrganizationProfile();
  const [serverError, setServerError] = useState<string | null>(null);

  const { register, handleSubmit, reset, formState: { errors, isSubmitting } } = useForm<OrganizationProfileFormValues>({
    resolver: zodResolver(organizationProfileSchema),
  });

  useEffect(() => {
    if (data) {
      reset({
        supportEmail: data.profile.supportEmail ?? "",
        supportPhone: data.profile.supportPhone ?? "",
        addressLine: data.profile.addressLine ?? "",
        logoUrl: data.profile.logoUrl ?? "",
        timeZoneId: data.profile.timeZoneId,
        businessHours: data.profile.businessHours ?? "",
      });
    }
  }, [data, reset]);

  async function onSubmit(values: OrganizationProfileFormValues) {
    setServerError(null);
    try {
      await updateProfile.mutateAsync({
        supportEmail: values.supportEmail || null,
        supportPhone: values.supportPhone || null,
        addressLine: values.addressLine || null,
        logoUrl: values.logoUrl || null,
        timeZoneId: values.timeZoneId,
        businessHours: values.businessHours || null,
      });
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  if (isLoading) {
    return (
      <div className="space-y-3" role="status" aria-live="polite">
        <span className="sr-only">Loading</span>
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-10 w-1/2" />
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="max-w-xl space-y-4" noValidate>
      <div className="space-y-2">
        <Label htmlFor="supportEmail">Support Email</Label>
        <Input id="supportEmail" aria-invalid={!!errors.supportEmail} {...register("supportEmail")} />
        {errors.supportEmail && <FieldError id="supportEmail-error">{errors.supportEmail.message}</FieldError>}
      </div>
      <div className="space-y-2">
        <Label htmlFor="supportPhone">Support Phone</Label>
        <Input id="supportPhone" {...register("supportPhone")} />
      </div>
      <div className="space-y-2">
        <Label htmlFor="addressLine">Address</Label>
        <Input id="addressLine" {...register("addressLine")} />
      </div>
      <div className="space-y-2">
        <Label htmlFor="logoUrl">Logo URL</Label>
        <Input id="logoUrl" {...register("logoUrl")} />
      </div>
      <div className="space-y-2">
        <Label htmlFor="timeZoneId">Timezone</Label>
        <Input id="timeZoneId" aria-invalid={!!errors.timeZoneId} {...register("timeZoneId")} />
        {errors.timeZoneId && <FieldError id="timeZoneId-error">{errors.timeZoneId.message}</FieldError>}
      </div>
      <div className="space-y-2">
        <Label htmlFor="businessHours">Business Hours</Label>
        <Input id="businessHours" placeholder="9:00 AM - 6:00 PM" {...register("businessHours")} />
      </div>
      {serverError && <FieldError id="profile-server-error">{serverError}</FieldError>}
      <Button type="submit" disabled={isSubmitting}>
        {isSubmitting ? <Spinner className="h-4 w-4" /> : "Save Profile"}
      </Button>
    </form>
  );
}
