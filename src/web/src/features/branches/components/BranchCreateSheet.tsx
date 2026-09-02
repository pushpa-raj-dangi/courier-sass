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
import { GeographyPicker, type GeographyValue } from "@/features/branches/GeographyPicker";
import { createBranchSchema, type CreateBranchFormValues } from "@/features/branches/schema";
import { useCreateBranch } from "@/features/branches/hooks/useCreateBranch";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useBranchPanelStore } from "@/features/branches/store/branchPanelStore";

const BRANCH_TYPES = ["HeadOffice", "Hub", "Branch", "PickupPoint", "Warehouse"] as const;

export function BranchCreateSheet() {
  const panel = useBranchPanelStore((s) => s.panel);
  const close = useBranchPanelStore((s) => s.close);
  const openDetail = useBranchPanelStore((s) => s.openDetail);
  const isOpen = panel.mode === "create";

  const createBranch = useCreateBranch();
  const { data: existingBranches } = useBranches();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    control,
    watch,
    setValue,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateBranchFormValues>({ resolver: zodResolver(createBranchSchema) });

  const geographyValue: GeographyValue = {
    provinceId: watch("provinceId") ?? null,
    districtId: watch("districtId") ?? null,
    localLevelId: watch("localLevelId") ?? null,
    wardNumber: watch("wardNumber") ?? null,
  };

  async function onSubmit(values: CreateBranchFormValues) {
    setServerError(null);
    try {
      const created = await createBranch.mutateAsync(values);
      reset();
      openDetail(created.id);
    } catch {
      setServerError("Couldn't create the branch. Check the fields and try again.");
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
          <SheetTitle>New Branch</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="code">Code</Label>
              <Input
                id="code"
                aria-invalid={!!errors.code}
                aria-describedby={errors.code ? "code-error" : undefined}
                {...register("code")}
              />
              {errors.code && <FieldError id="code-error">{errors.code.message}</FieldError>}
            </div>
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
          </div>
          <div className="space-y-2">
            <Label htmlFor="type">Type</Label>
            <Controller
              name="type"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger
                    id="type"
                    aria-label="Type"
                    aria-invalid={!!errors.type}
                    aria-describedby={errors.type ? "type-error" : undefined}
                  >
                    <SelectValue placeholder="Select type" />
                  </SelectTrigger>
                  <SelectContent>
                    {BRANCH_TYPES.map((t) => (
                      <SelectItem key={t} value={t}>
                        {t}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {errors.type && <FieldError id="type-error">{errors.type.message}</FieldError>}
          </div>
          <GeographyPicker
            value={geographyValue}
            onChange={(next) => {
              setValue("provinceId", next.provinceId ?? "");
              setValue("districtId", next.districtId ?? "");
              setValue("localLevelId", next.localLevelId ?? "");
              setValue("wardNumber", next.wardNumber ?? undefined as never);
            }}
          />
          <div className="space-y-2">
            <Label htmlFor="addressLine">Address</Label>
            <Input
              id="addressLine"
              aria-invalid={!!errors.addressLine}
              aria-describedby={errors.addressLine ? "addressLine-error" : undefined}
              {...register("addressLine")}
            />
            {errors.addressLine && (
              <FieldError id="addressLine-error">{errors.addressLine.message}</FieldError>
            )}
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="phone">Phone</Label>
              <Input id="phone" {...register("phone")} />
            </div>
            <div className="space-y-2">
              <Label htmlFor="operatingHours">Operating Hours</Label>
              <Input id="operatingHours" placeholder="9:00 AM - 6:00 PM" {...register("operatingHours")} />
            </div>
          </div>
          <div className="space-y-2">
            <Label htmlFor="parentBranchId">Parent Branch</Label>
            <Controller
              name="parentBranchId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="parentBranchId" aria-label="Parent branch">
                    <SelectValue placeholder="None" />
                  </SelectTrigger>
                  <SelectContent>
                    {existingBranches?.map((b) => (
                      <SelectItem key={b.id} value={b.id}>
                        {b.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </div>
          {serverError && <FieldError id="branch-server-error">{serverError}</FieldError>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Branch"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
