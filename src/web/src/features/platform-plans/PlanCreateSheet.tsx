"use client";

import { useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import type { z } from "zod";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { createPlanSchema, type CreatePlanInput } from "@/features/platform-plans/schema";
import { useCreatePlan } from "@/features/platform-plans/hooks/useCreatePlan";
import { usePlanPanelStore } from "@/features/platform-plans/store/planPanelStore";

// Follows BranchCreateSheet.tsx / VehicleCreateSheet.tsx's create-mode composition: react-hook-form
// + zodResolver, plain Label/Input/FieldError per field (this codebase doesn't use shadcn's <Form>
// wrapper), Checkbox for the single boolean field per RoleCreateSheet.tsx's pattern. Optional
// numeric fields use setValueAs so a blank input reaches the form as undefined (unlimited) rather
// than NaN or 0 — see MerchantShipmentCreateSheet.tsx's lengthCm/widthCm/heightCm fields.
export function PlanCreateSheet() {
  const isOpen = usePlanPanelStore((s) => s.isCreateOpen);
  const close = usePlanPanelStore((s) => s.close);

  const createPlan = useCreatePlan();
  const [serverError, setServerError] = useState<string | null>(null);

  // useForm is typed with the schema's INPUT shape (z.input — pre-coerce numeric fields can be
  // `unknown`/string from raw HTML inputs) as TFieldValues and CreatePlanInput (z.output) as
  // TTransformedValues, matching zodResolver's Resolver<Input, Context, Output> signature — using
  // CreatePlanInput for both would fail to type-check because z.coerce.number() makes the schema's
  // input and output types diverge.
  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<z.input<typeof createPlanSchema>, unknown, CreatePlanInput>({
    resolver: zodResolver(createPlanSchema),
    defaultValues: { isDefault: false },
  });

  async function onSubmit(values: CreatePlanInput) {
    setServerError(null);
    try {
      await createPlan.mutateAsync(values);
      reset({ isDefault: false });
      close();
    } catch {
      setServerError("Couldn't create the plan. Check the fields and try again.");
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
          <SheetTitle>New Plan</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="name">Name</Label>
              <Input
                id="name"
                aria-invalid={!!errors.name}
                aria-describedby={errors.name ? "name-error" : undefined}
                {...register("name")}
              />
              {errors.name && <FieldError id="name-error">{errors.name.message}</FieldError>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="slug">Slug</Label>
              <Input
                id="slug"
                aria-invalid={!!errors.slug}
                aria-describedby={errors.slug ? "slug-error" : undefined}
                {...register("slug")}
              />
              {errors.slug && <FieldError id="slug-error">{errors.slug.message}</FieldError>}
            </div>
          </div>
          <div className="space-y-2">
            <Label htmlFor="monthlyPriceNpr">Monthly Price (NPR)</Label>
            <Input
              id="monthlyPriceNpr"
              type="number"
              step="0.01"
              aria-invalid={!!errors.monthlyPriceNpr}
              aria-describedby={errors.monthlyPriceNpr ? "monthlyPriceNpr-error" : undefined}
              {...register("monthlyPriceNpr", { valueAsNumber: true })}
            />
            {errors.monthlyPriceNpr && (
              <FieldError id="monthlyPriceNpr-error">{errors.monthlyPriceNpr.message}</FieldError>
            )}
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <div className="space-y-2">
              <Label htmlFor="maxBranches">Max Branches</Label>
              <Input
                id="maxBranches"
                type="number"
                placeholder="Unlimited"
                aria-invalid={!!errors.maxBranches}
                aria-describedby={errors.maxBranches ? "maxBranches-error" : undefined}
                {...register("maxBranches", { setValueAs: (v) => (v === "" ? undefined : Number(v)) })}
              />
              {errors.maxBranches && (
                <FieldError id="maxBranches-error">{errors.maxBranches.message}</FieldError>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="maxStaffUsers">Max Staff Users</Label>
              <Input
                id="maxStaffUsers"
                type="number"
                placeholder="Unlimited"
                aria-invalid={!!errors.maxStaffUsers}
                aria-describedby={errors.maxStaffUsers ? "maxStaffUsers-error" : undefined}
                {...register("maxStaffUsers", { setValueAs: (v) => (v === "" ? undefined : Number(v)) })}
              />
              {errors.maxStaffUsers && (
                <FieldError id="maxStaffUsers-error">{errors.maxStaffUsers.message}</FieldError>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="maxMonthlyShipments">Max Monthly Shipments</Label>
              <Input
                id="maxMonthlyShipments"
                type="number"
                placeholder="Unlimited"
                aria-invalid={!!errors.maxMonthlyShipments}
                aria-describedby={errors.maxMonthlyShipments ? "maxMonthlyShipments-error" : undefined}
                {...register("maxMonthlyShipments", { setValueAs: (v) => (v === "" ? undefined : Number(v)) })}
              />
              {errors.maxMonthlyShipments && (
                <FieldError id="maxMonthlyShipments-error">{errors.maxMonthlyShipments.message}</FieldError>
              )}
            </div>
          </div>
          <div className="flex items-center gap-2">
            <Controller
              name="isDefault"
              control={control}
              render={({ field }) => (
                <Checkbox id="isDefault" checked={field.value} onCheckedChange={field.onChange} />
              )}
            />
            <Label htmlFor="isDefault" className="text-sm font-normal text-muted-foreground">
              Make this the default plan
            </Label>
          </div>
          {serverError && <FieldError id="plan-server-error">{serverError}</FieldError>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Plan"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
