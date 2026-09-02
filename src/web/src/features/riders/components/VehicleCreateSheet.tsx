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
import { createVehicleSchema, type CreateVehicleFormValues } from "@/features/riders/schema";
import { useCreateVehicle } from "@/features/riders/hooks/useCreateVehicle";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";

export function VehicleCreateSheet() {
  const panel = useRiderPanelStore((s) => s.panel);
  const close = useRiderPanelStore((s) => s.close);
  const isOpen = panel.mode === "create-vehicle";

  const createVehicle = useCreateVehicle();
  const { data: riders } = useRiders();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateVehicleFormValues>({ resolver: zodResolver(createVehicleSchema) });

  async function onSubmit(values: CreateVehicleFormValues) {
    setServerError(null);
    try {
      await createVehicle.mutateAsync(values);
      reset();
      close();
    } catch {
      setServerError("Couldn't create the vehicle. Check the fields and try again.");
    }
  }

  return (
    <Sheet
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) close();
      }}
    >
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>New Vehicle</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="type">Type</Label>
              <Input
                id="type"
                placeholder="Motorcycle"
                aria-invalid={!!errors.type}
                aria-describedby={errors.type ? "type-error" : undefined}
                {...register("type")}
              />
              {errors.type && <FieldError id="type-error">{errors.type.message}</FieldError>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="plateNumber">Plate Number</Label>
              <Input
                id="plateNumber"
                aria-invalid={!!errors.plateNumber}
                aria-describedby={errors.plateNumber ? "plateNumber-error" : undefined}
                {...register("plateNumber")}
              />
              {errors.plateNumber && <FieldError id="plateNumber-error">{errors.plateNumber.message}</FieldError>}
            </div>
          </div>
          <div className="space-y-2">
            <Label htmlFor="riderId">Assigned Rider</Label>
            <Controller
              name="riderId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="riderId" aria-label="Assigned rider">
                    <SelectValue placeholder="None" />
                  </SelectTrigger>
                  <SelectContent>
                    {riders?.map((r) => (
                      <SelectItem key={r.id} value={r.id}>{r.licenseNumber}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </div>
          {serverError && <FieldError id="vehicle-server-error">{serverError}</FieldError>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Vehicle"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
