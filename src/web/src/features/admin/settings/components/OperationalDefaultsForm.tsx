"use client";

import { useEffect, useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { Skeleton } from "@/components/ui/skeleton";
import { useOrganizationSettings } from "@/features/admin/settings/hooks/useOrganizationSettings";
import { useUpdateOperationalDefaults } from "@/features/admin/settings/hooks/useUpdateOperationalDefaults";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useServiceTypes } from "@/features/shipments/hooks/useServiceTypes";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";

interface DefaultsFormValues {
  defaultServiceTypeId: string;
  defaultOriginBranchId: string;
}

const NONE_VALUE = "__none__";

export function OperationalDefaultsForm() {
  const { data, isLoading } = useOrganizationSettings();
  const { data: branches } = useBranches();
  const { data: serviceTypes } = useServiceTypes();
  const updateDefaults = useUpdateOperationalDefaults();
  const [serverError, setServerError] = useState<string | null>(null);

  const { control, handleSubmit, reset, formState: { isSubmitting } } = useForm<DefaultsFormValues>({
    defaultValues: { defaultServiceTypeId: NONE_VALUE, defaultOriginBranchId: NONE_VALUE },
  });

  useEffect(() => {
    if (data) {
      reset({
        defaultServiceTypeId: data.defaults.defaultServiceTypeId ?? NONE_VALUE,
        defaultOriginBranchId: data.defaults.defaultOriginBranchId ?? NONE_VALUE,
      });
    }
  }, [data, reset]);

  async function onSubmit(values: DefaultsFormValues) {
    setServerError(null);
    try {
      await updateDefaults.mutateAsync({
        defaultServiceTypeId: values.defaultServiceTypeId === NONE_VALUE ? null : values.defaultServiceTypeId,
        defaultOriginBranchId: values.defaultOriginBranchId === NONE_VALUE ? null : values.defaultOriginBranchId,
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
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="max-w-xl space-y-4" noValidate>
      <div className="space-y-2">
        <Label htmlFor="defaultServiceTypeId">Default Service Type</Label>
        <Controller
          name="defaultServiceTypeId"
          control={control}
          render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger id="defaultServiceTypeId" aria-label="Default service type"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value={NONE_VALUE}>None</SelectItem>
                {serviceTypes?.map((s) => (
                  <SelectItem key={s.id} value={s.id}>{s.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
      </div>
      <div className="space-y-2">
        <Label htmlFor="defaultOriginBranchId">Default Origin Branch</Label>
        <Controller
          name="defaultOriginBranchId"
          control={control}
          render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger id="defaultOriginBranchId" aria-label="Default origin branch"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value={NONE_VALUE}>None</SelectItem>
                {branches?.map((b) => (
                  <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
      </div>
      {serverError && <FieldError id="defaults-server-error">{serverError}</FieldError>}
      <Button type="submit" disabled={isSubmitting}>
        {isSubmitting ? <Spinner className="h-4 w-4" /> : "Save Defaults"}
      </Button>
    </form>
  );
}
